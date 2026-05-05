using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests;
using ServiceApi.Responses;
using System.Data;
using System.Data.Common;

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
    public abstract IAsyncEnumerable<TResponse> ExecuteAsync(TRequest request);

    protected virtual IAsyncEnumerable<TResponse> ExecuteQueryAsync(
        string sql,
        Func<DbDataReader, TResponse> mapFunc)
        => ExecuteQueryAsync(sql, _ => { }, mapFunc);

    // --- 既存メソッドの統合： DataReader 版を呼び出す ---
    protected virtual async IAsyncEnumerable<TResponse> ExecuteQueryAsync(
        string sql,
        Action<OracleParameterCollection> bindAction,
        Func<DbDataReader, TResponse> mapFunc)
    {
        // 戻り値が IAsyncEnumerable なので await foreach で繋ぐ
        await foreach (var reader in ExecuteQueryAsync(sql, bindAction))
        {
            yield return mapFunc(reader);
        }
    }

    /*
     * ExecuteQueryAsync(string sql)（引数1つのもの）は、
     * 基本の ExecuteQueryAsync(sql, _ => {}) で代用できるため、
     * 具象クラスでの利用頻度が低ければ削除しても問題ありません。
     */
    protected virtual IAsyncEnumerable<DbDataReader> ExecuteQueryAsync(string sql)
        => ExecuteQueryAsync(sql, _ => { });

    // --- マッピング処理を外（具象クラス）で実装できるようにするエントリポイント ---
    protected virtual async IAsyncEnumerable<DbDataReader> ExecuteQueryAsync(
        string sql,
        Action<OracleParameterCollection> bindAction)
    {
#if true
        // 接続状態を確認 
        if (this.Connection is { State: ConnectionState.Closed })
        {
            await this.Connection.OpenAsync();
        }

        // コマンド作成・パラメータのバインド前に名前解決を有効化
        using var cmd = new OracleCommand(sql, Connection) { BindByName = true };
#else
        // 接続状態を確認 
        if (this.Connection.State != ConnectionState.Open)
        {
            await this.Connection.OpenAsync();
        }

        // コマンド作成
        using var cmd = new OracleCommand(sql, this.Connection);

        // パラメータをバインドする前に名前解決を有効化
        cmd.BindByName = true;
#endif

        // デリゲートを実行してパラメータを埋め込む
        // bindAction(cmd.Parameters) により、具象クラス側で定義した詰め物処理が動く
        bindAction(cmd.Parameters);

        // CommandBehavior.Default でも良いですが、念のため
        using var reader = await cmd.ExecuteReaderAsync();

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

        while (await reader.ReadAsync())
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

#if true
    /// <summary>
    /// 同期的リソース解放
    /// </summary>
    public void Dispose()
    {
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
        if (this.Connection != null) { await this.Connection.DisposeAsync(); }
        GC.SuppressFinalize(this);
    }
#else
    /// <summary>
    /// 同期的リソース解放
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

    /*
     * バッチプログラムにおいて大量データを扱う場合、DB接続の解放待ち（I/O待ち）で
     * スレッドが止まるのを防ぐため、DisposeAsync を実装します。
     * ApiExecutor 側で await enumerator.DisposeAsync() が実行されると、
     * この ServiceBase.DisposeAsync まで非同期の波が伝わり、理想的なリソース解放が行われます。
     */

    /// <summary>
    /// 非同期的リソース解放
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        /*
         * ?.（null条件演算子）と await の組み合わせは、対象が null の場合に 
         * null（あるいは完了済みの ValueTask）として扱われるため、
         * 実行時に例外を投げることなく安全にスルーされます。
         */
        if (Connection is { State: ConnectionState.Open })
        {
            await Connection.CloseAsync();
        }
        await (Connection?.DisposeAsync() ?? ValueTask.CompletedTask);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (Connection?.State == ConnectionState.Open)
            {
                Connection.Close();
            }
            Connection?.Dispose();
        }
    }
#endif
}
