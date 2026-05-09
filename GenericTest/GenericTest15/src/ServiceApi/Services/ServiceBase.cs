using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests;
using ServiceApi.Responses;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace ServiceApi.Services;

public abstract class ServiceBase<TRequest, TResponse>(
    string connectionString,
    int fetchRows = 100
) : IApiService<TRequest, TResponse>, IDisposable, IAsyncDisposable
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    // Oracleコネクションオブジェクトを保持（プライマリコンストラクタの引数を使用）
    protected OracleConnection Connection { get; } = new(connectionString);

    // 具象クラスに実装を強制するエントリポイント
    public abstract IAsyncEnumerable<TResponse> ExecuteAsync(
        IEnumerable<TRequest> requests,
        CancellationToken ct = default);

    protected virtual IAsyncEnumerable<TResponse> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        Func<DbDataReader, TResponse> mapFunc,
        CancellationToken ct = default)
        => ExecuteQueryAsync(sql, requests, (_, _) => { }, mapFunc, ct);

    // --- 既存メソッドの統合： DataReader 版を呼び出す ---
    protected virtual async IAsyncEnumerable<TResponse> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        Action<OracleParameterCollection, TRequest> bindAction,
        Func<DbDataReader, TResponse> mapFunc,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 戻り値が IAsyncEnumerable であるため await foreach で繋ぐ
        await foreach (var reader in ExecuteQueryAsync(sql, requests, bindAction, ct))
        {
            yield return mapFunc(reader);
        }
    }

    protected virtual IAsyncEnumerable<DbDataReader> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        CancellationToken ct = default)
        => ExecuteQueryAsync(sql, requests, (_, _) => { }, ct);

    // マッピング処理を具象クラスで実装できるようにするエントリポイント
    protected virtual async IAsyncEnumerable<DbDataReader> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        Action<OracleParameterCollection, TRequest> bindAction,
        [EnumeratorCancellation] CancellationToken ct = default) // 属性を追加
    {
        // 接続状態を確認 
        if (this.Connection is { State: ConnectionState.Closed })
        {
            await this.Connection.OpenAsync(ct);
        }

        // コマンド作成：ループの外で作成することでSQL解析(Parse)コストを抑える
        /*
         * foreachループの外側でOracleCommandをusing生成します。
         * これにより、同じSQL文であればOracle側でのカーソル再利用が促され
         * バインドパラメータだけを入れ替えて実行する形になっています。
         */
        using var cmd = new OracleCommand(sql, Connection) { BindByName = true };

        foreach (TRequest req in requests)
        {
            // キャンセル要求があれば例外を投げてループを抜ける
            /*
             * ループの開始直後（ct.ThrowIfCancellationRequested()）と
             * フェッチ処理（reader.ReadAsync(ct)）の両方にCancellationToken を適用します。
             * 大量のリクエスト配列が渡された場合でも、安全かつ迅速に中断が可能です。
             */
            ct.ThrowIfCancellationRequested();

            // パラメータのクリアと再バインド
            cmd.Parameters.Clear();
            bindAction(cmd.Parameters, req);

            // SQL実行
            // using を使うことで、次のリクエストのループに進む際、
            // 前のリクエストの DataReader が確実に破棄(Close)される
            /*
             * ループの中でusing var readerと記述します。これにより
             * 次のTRequestの処理（ExecuteReaderAsync）に移る前に
             * 現在のreaderが閉じられます。これはOracleの
             * 「最大オープン・カーソル数」制限を回避するために不可欠です。
             */
            using var reader = await cmd.ExecuteReaderAsync(ct);

            // FetchSizeの最適化
            // 確定した RowSize を使って、FetchSizeを最適化する
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
            reader.FetchSize = reader.RowSize * fetchRows;

            while (await reader.ReadAsync(ct))
            {
                // DataReaderそのものを yield return する
                // 1行読み込むごとに呼び出し元へ yield return する
                /*
                 * 従来の "List<TResponse>" を返す方式と、今回の "IAsyncEnumerable<TResponse>"
                 * を返す方式の最大の違いは、**メモリ上でのデータの持ち方**です。
                 * **List方式**: DBから100万件の結果がある場合、100万件すべてをメモリ (List) に
                 * 格納し終わるまでメインプログラムにデータは一切渡されません。
                 * **ストリーム方式**: DBから1行読み込むごとに、そのデータが即座に呼び出し元 ("Program.cs")
                 * へ「配送」されます。
                 * この「配送」を実現しているのが **"yield return"** です。
                 * このキーワードは、メソッドを終了させずに「一旦この値を呼び出し元に渡し、
                 * 次の要求があったら続きから再開する」という特殊な動きを可能にしています。
                 */
                yield return reader;
            }
        }
        // 1つのリクエストが終わるたびに reader.Dispose() が走り、
        // 全てのリクエストが終わると cmd.Dispose() が走る
    }

    /// <summary>
    /// 同期的リソース解放
    /// </summary>
    public void Dispose()
    {
        // Connection.Close() を明示的に呼んでから Dispose すると、
        // Oracleのセッションが即座に解放されやすくなり、DB側に優しいです。
        if (this.Connection is { State: ConnectionState.Open }) { this.Connection.Close(); }
        this.Connection?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 非同期的リソース解放
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (this.Connection is { State: ConnectionState.Open }) { await this.Connection.CloseAsync(); }
        if (this.Connection is not null) { await this.Connection.DisposeAsync(); }
        GC.SuppressFinalize(this);
    }
}
