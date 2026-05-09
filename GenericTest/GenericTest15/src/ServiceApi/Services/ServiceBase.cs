using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests;
using ServiceApi.Responses;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace ServiceApi.Services;

/// <summary>
/// サービスクラス（基底）
/// </summary>
/// <typeparam name="TRequest">リクエストクラス</typeparam>
/// <typeparam name="TResponse">レスポンスクラス</typeparam>
/// <param name="connectionString">DB接続文字列</param>
/// <param name="fetchRows">フェッチ行数指定</param>
public abstract class ServiceBase<TRequest, TResponse>(
    string connectionString,
    int fetchRows = 100
) : IApiService<TRequest, TResponse>, IDisposable, IAsyncDisposable
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    /// <summary>
    /// Oracleコネクション
    /// </summary>
    protected OracleConnection Connection { get; } = new(connectionString);

    // 具象クラスに実装を強制するエントリポイント
    /// <summary>
    /// サービスエントリポイント
    /// </summary>
    /// <param name="requests">リクエスト配列</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns></returns>
    public abstract IAsyncEnumerable<TResponse> ExecuteAsync(
        IEnumerable<TRequest> requests,
        CancellationToken ct = default);

    /// <summary>
    /// DB検索処理（ストリーム版）
    /// </summary>
    /// <param name="sql">SQL文</param>
    /// <param name="requests">リクエスト配列</param>
    /// <param name="mapFunc">マッピング用デリゲート</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>DB検索結果</returns>
    protected virtual IAsyncEnumerable<TResponse> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        Func<DbDataReader, TResponse> mapFunc,
        CancellationToken ct = default)
        => ExecuteQueryAsync(sql, requests, (_, _) => { }, mapFunc, ct);

    /// <summary>
    /// DB検索処理（ストリーム版）
    /// </summary>
    /// <param name="sql">SQL文</param>
    /// <param name="requests">リクエスト配列</param>
    /// <param name="bindAction">パラメータバインド用アクション</param>
    /// <param name="mapFunc">マッピング用デリゲート</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>DB検索結果</returns>
    protected virtual async IAsyncEnumerable<TResponse> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        Action<OracleParameterCollection, TRequest> bindAction,
        Func<DbDataReader, TResponse> mapFunc,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 戻り値がIAsyncEnumerableであるためawait foreachで繋ぐ
        await foreach (var reader in ExecuteQueryAsync(sql, requests, bindAction, ct))
        {
            yield return mapFunc(reader);
        }
    }

    /// <summary>
    /// DB検索処理（DataReader版）
    /// </summary>
    /// <param name="sql">SQL文</param>
    /// <param name="requests">リクエスト配列</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>DB検索結果</returns>
    protected virtual IAsyncEnumerable<DbDataReader> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        CancellationToken ct = default)
        => ExecuteQueryAsync(sql, requests, (_, _) => { }, ct);

    // マッピング処理を具象クラスで実装できるようにするエントリポイント
    /// <summary>
    /// DB検索処理（DataReader版）
    /// </summary>
    /// <param name="sql">SQL文</param>
    /// <param name="requests">リクエスト配列</param>
    /// <param name="bindAction">パラメータバインド用アクション</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>DB検索結果</returns>
    protected virtual async IAsyncEnumerable<DbDataReader> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        Action<OracleParameterCollection, TRequest> bindAction,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 接続状態を確認 
        if (this.Connection is { State: ConnectionState.Closed })
        {
            await this.Connection.OpenAsync(ct);
        }

        // コマンド作成：ループの外で作成することによりSQL解析(Parse)コストを抑える
        /*
         * foreachループの外側でOracleCommandをusing生成することにより
         * 同じSQL文であればOracle側でのカーソル再利用が促され
         * バインドパラメータだけを入れ替えて実行することができる
         */
        using var cmd = new OracleCommand(sql, Connection) { BindByName = true };

        foreach (TRequest req in requests)
        {
            // キャンセル要求があれば例外を投げてループを抜ける
            /*
             * ループの開始直後（ct.ThrowIfCancellationRequested()）と
             * フェッチ処理（reader.ReadAsync(ct)）の両方にCancellationTokenを適用する
             * 大量のリクエスト配列が渡された場合でも安全かつ迅速に中断が可能となる
             */
            ct.ThrowIfCancellationRequested();

            // パラメータのクリアと再バインド
            cmd.Parameters.Clear();
            bindAction(cmd.Parameters, req);

            // SQL実行
            /*
             * ループの中でusing var readerと記述することで、次のTRequestの処理
             * （ExecuteReaderAsync）に移る前に現在のreaderがクローズされる
             * これはOracleの「最大オープン・カーソル数」制限を回避するために不可欠
             */
            using var reader = await cmd.ExecuteReaderAsync(ct);

            // FetchSizeの最適化
            // 確定した RowSize を使ってFetchSizeを最適化する
            /*
             * FetchRows (まとめて取得する行数) は、100-500程度が一般的。
             * 対象のテーブルが非常に「横に長い（カラム数が多い、あるいは1カラムの定義サイズが大きい）」
             * 場合はRowSize自体が大きくなる。その際、あまりに行数を大きくしすぎると
             * 1回の通信で数MBものメモリを消費し、逆にスワップが発生して遅くなることがある。
             * まずは100程度で試してみて、本番環境のネットワーク遅延が大きい (DBサーバーが遅い) 場合は
             * この数値を徐々に増やして調整するのがよい。
             * ベストプラクティスでは、1つのクエリにつき1MB-2MB程度のバッファを確保するのが
             * 最もコストパフォーマンスが良い (速度向上の幅が大きくメモリ負荷が低い)とされる。
             * RowSizeが小さい (例：100 bytes) 場合: FetchRows = 10000 くらいまで上げてもOK。
             * RowSizeが大きい (例：10,000 bytes) 場合: FetchRows = 100 くらいが適切。
             * 特別な制約がない限り、初期設定としては100程度が推奨。
             * これだけでデフォルト状態 (FetchSize = 65536 バイトなど) に比べて
             * ネットワーク通信回数が大幅に削減され十分な高速化の恩恵を受けられる。
             */
            reader.FetchSize = reader.RowSize * fetchRows;

            while (await reader.ReadAsync(ct))
            {
                // 1行読み込むごとに呼び出し元へ yield return する
                /*
                 * 従来の"List<TResponse>"を返す方式と"IAsyncEnumerable<TResponse>"
                 * を返す方式の最大の違いは、メモリ上でのデータの持ち方です。
                 * ・List方式: DBから100万件の結果がある場合、100万件すべてをメモリ (List) に
                 *   格納し終わるまでメインプログラムにデータは一切渡されません。
                 * ・ストリーム方式: DBから1行読み込むごとに、そのデータが即座に呼び出し元へ
                 *  「配送」されます。
                 *   この「配送」を実現しているのが"yield return"です。
                 * このキーワードは、メソッドを終了させずに「一旦この値を呼び出し元に渡し、
                 * 次の要求があったら続きから再開する」という特殊な動きを可能にしています。
                 */
                yield return reader;
            }
        }
        // 1つのリクエストが終わるたびにreader.Dispose()が走り、
        // 全てのリクエストが終わるとcmd.Dispose()が走る
    }

    /// <summary>
    /// 同期的リソース解放
    /// </summary>
    public void Dispose()
    {
        // Connection.Close()を明示的に呼んでからDisposeすると
        // Oracleのセッションが即座に解放されやすくなりDB側の負担を軽減できる
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
