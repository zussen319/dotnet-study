using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests;
using ServiceApi.Responses;
using System.Data;

namespace ServiceApi.Services;

public abstract class ServiceBase<TRequest, TResponse>(
    string connectionString,
    int fetchRows = 100
) : IApiService<TRequest, TResponse>, IDisposable
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    // Oracle接続オブジェクトを保持（プライマリコンストラクタの引数を使用）
    protected OracleConnection Connection { get; } = new(connectionString);

    protected int FetchRows { get; } = fetchRows;

    // 具象クラスに実装を強制するエントリポイント
    public abstract IAsyncEnumerable<TResponse> ExecuteAsync(TRequest request);

    protected virtual IAsyncEnumerable<TResponse> ExecuteQueryAsync(
        string sql,
        Func<IDataRecord, TResponse> mapFunc
    ) => ExecuteQueryAsync(sql, _ => {  /* 何もしない */ }, mapFunc);

    // 共通の実行ロジック：パラメータ設定とマッパーをラムダで受け取る
    protected virtual IAsyncEnumerable<TResponse> ExecuteQueryAsync(
        string sql,
        Action<OracleParameterCollection> bindAction,
        Func<IDataRecord, TResponse> mapFunc)
    {
        /*
         * メソッドにasync キーワードが不要な理由
         * C#において async キーワードが必要なのは、そのメソッド内で await を直接使用する場合のみ です。
         * 今回の ExecuteQueryAsync(string sqlId, ...) メソッドの役割を見てみましょう。
         * （1. -- SQLをリソースから取ってくる）※ 呼び出し元で実行
         * 2. OracleCommand を作る
         * 3. パラメータをセットする
         * 4. 別のメソッドが作った IAsyncEnumerable をそのまま値として返す
         * このメソッド自体は、DBに接続したりデータを読み取ったりという
         * 「待機 (await) が必要な重い処理」を自分では行っていません。
         * 「重い処理」を行うのは、呼び出している先のExecuteQueryAsync(OracleCommand cmd) です。
         */

        // コマンド作成
        var cmd = new OracleCommand(sql);

        // パラメータをバインドする前に名前解決を有効にする
        cmd.BindByName = true;

        // デリゲートを実行してパラメータを埋め込む
        // bindAction(cmd.Parameters) により、具象クラス側で定義した詰め物処理が動く
        bindAction(cmd.Parameters);

        /*
         * await をしていない理由
         * ExecuteQueryAsync(cmd) を呼び出す際、await を付けていないのは
         * 戻り値が Task ではなく IAsyncEnumerable だからです。
         * Task の場合 (await が必要)
         * Task<T> を返すメソッドは、「将来いつか完了する1つの結果」を約束するものです。
         * その中身を取り出すには await して完了を待つ必要があります。
         * IAsyncEnumerable<T> は、「データの蛇口」のようなものです。
         * return ExecuteQueryAsync(cmd); と書いた瞬間、まだDBには1行もアクセスしていません。
         * ただ「この蛇口をひねれば、データが流れてきますよ」という接続情報と実行プランが詰まった
         * オブジェクトを返しているだけです。
         * 実際に非同期の待機が発生するのは、このメソッドの戻り値を受け取った側 (Program.cs など) が
         * await foreach を開始した瞬間 です。
         */
        return ExeuteQueyAsync(cmd, mapFunc);
    }

    protected async virtual IAsyncEnumerable<TResponse> ExeuteQueyAsync(
        OracleCommand cmd,
        Func<IDataRecord, TResponse>mapFunc)
    {
        // 1. 接続状態を確認 (共通の Connection メンバを使用)
        if (this.Connection.State != ConnectionState.Open)
        {
            await this.Connection.OpenAsync();
        }

        // 2. コマンドに接続を紐付け (外から来た cmd を使うだけ)
        cmd.Connection = this.Connection;
        
        // IAsyncEnumerable 内では using 句による Reader の保護が yield と組み合わさっても正しく動作します
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            // 確定した RowSize を使って、FetchSizeを最適化する
            // 例：一度に 100行 分をネットワークからまとめて持ってくる設定

            /*
             * FetchRows (まとめて取得する行数) は、100-500程度が一般的です。
             * もし対象のテーブルが非常に「横に長い（カラム数が多い、あるいは1カラムの定義サイズが大きい）」
             * 場合、RowSize 自体が大きくなります。その際、あまりに行数を大きくしすぎると、
             * 1回の通信で数MBものメモリを消費し、逆にスワップが発生して遅くなることがあります。
             * まずは 100 程度で試してみて、本番環境のネットワーク遅延が大きい (DBサーバーが遅い) 場合は
             * この数値を徐々に増やして調整するのが王道です。
             * Oracle公式や多くの現場でのベストプラクティスでは、1つのクエリにつき 1MB-2MB 程度 の
             * バッファを確保するのが最もコストパフォーマンスが良い (速度向上の幅が大きく、メモリ負荷が低い)
             * とされています。
             * RowSizeが小さい (例：100 bytes) 場合: FetchRows = 10000 くらいまで上げてもOK。
             * RowSizeが大きい (例：10,000 bytes) 場合: FetchRows = 100 くらいが適切。
             * 特別な制約がない限り、まずは 100 を設定してください。
             * これだけでデフォルト状態 (FetchSize = 65536 バイトなど) に比べて、
             * ネットワーク通信回数が大幅に削減され、十分な高速化の恩恵を受けられます。
             */
            reader.FetchSize = reader.RowSize * this.FetchRows;

            while(await reader.ReadAsync())
            {
                // 1行読み込むごとに呼び出し元へ yield return する
                /*
                 * ## 1.「全件読み込み」から「順次配送」への転換
                 * 従来の "List<TResponse>" を返す方式と、今回の "IAsyncEnumerable<TResponse>"
                 * を返す方式の最大の違いは、**メモリ上でのデータの持ち方**です。
                 * **List方式**: DBから100万件の結果がある場合、100万件すべてをメモリ (List) に
                 * 格納し終わるまでメインプログラムにデータは一切渡されません。
                 * **ストリーム方式**: DBから1行読み込むごとに、そのデータが即座に呼び出し元 ("Program.cs")
                 * へ「配送」されます。
                 * この「配送」を実現しているのが **"yield return"** です。このキーワードは、
                 * メソッドを終了させずに「一旦この値を呼び出し元に渡し、次の要求があったら続きから再開する」
                 * という特殊な動きを可能にしています。
                 */
                yield return mapFunc(reader);
            }
        }
    }

    /// <summary>
    /// リソースを解放します。
    /// </summary>
    public void Dispose()
    {
        // Connection.Close() を明示的に呼んでから Dispose すると、
        // Oracleのセッションが即座に解放されやすくなり、DB側に優しいです。

        if (this.Connection?.State == ConnectionState.Open) {
            // ここはCloseAsync()ではなくClose()でよい
            this.Connection.Close();
        }
        this.Connection?.Dispose();

        GC.SuppressFinalize(this);
    }
}
