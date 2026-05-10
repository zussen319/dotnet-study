using Oracle.ManagedDataAccess.Client;
using ServiceApi.Common;
using ServiceApi.Requests;
using ServiceApi.Responses;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace ServiceApi.Services;

/*
 * サービスクラス（基底）
 */
public abstract class ServiceBase<TRequest, TResponse>(
    string connectionString,
    int fetchRows = ApiConstants.DefaultFetchRows)
    : IApiService<TRequest, TResponse>, IDisposable
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    // Oracleコネクション
    protected OracleConnection Connection { get; } = new(connectionString);

    // サービス実行メソッド（Execute）
    // 具象クラスに実装を強制する
    public abstract IAsyncEnumerable<TResponse> ExecuteAsync(
        IEnumerable<TRequest> requests,
        CancellationToken ct = default);

    // DB検索（ExecuteQuery）
    protected virtual IAsyncEnumerable<TResponse> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        Func<DbDataReader, TResponse> mapFunc,
        CancellationToken ct = default)
        => ExecuteQueryAsync(sql, requests, (_, _) => { }, mapFunc, ct);

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
            // 呼び出し元から渡されたmapFuncでレスポンスオブジェクトを生成して返す
            yield return mapFunc(reader);
        }
    }

    protected virtual IAsyncEnumerable<DbDataReader> ExecuteQueryAsync(
        string sql,
        IEnumerable<TRequest> requests,
        CancellationToken ct = default)
        => ExecuteQueryAsync(sql, requests, (_, _) => { }, ct);

    // マッピング処理を具象クラスで実装できるようにする
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

        // コマンド作成
        // ループの外で作成することによりSQL解析(Parse)コストを抑える
        /*
         * foreachループの外側でOracleCommandをusing生成することにより
         * 同じSQL文であればOracle側でのカーソル再利用が促され
         * バインドパラメータだけを入れ替えて実行することができる
         */
        using var command = new OracleCommand(sql, this.Connection) { BindByName = true };

        foreach (TRequest request in requests)
        {
            // キャンセル要求を受け取ったら例外をスローしループを抜ける
            /*
             * ループの開始直後（ct.ThrowIfCancellationRequested()）と
             * フェッチ処理（reader.ReadAsync(ct)）の両方にCancellationTokenを適用する
             * 大量のリクエスト配列が渡された場合でも安全かつ迅速に中断が可能となる
             */
            ct.ThrowIfCancellationRequested();

            // パラメータのクリアとバインド
            // 呼び出し元から渡されたbindActionを実行する
            command.Parameters.Clear();
            bindAction(command.Parameters, request);

            // SQL実行
            /*
             * ループの中でusing var readerと記述することで、次のリクエストの処理
             * （ExecuteReaderAsync）に移る前に現在のreaderがクローズされる
             * Oracleの「最大オープンカーソル数」制限を回避するために不可欠
             */
            using var reader = await command.ExecuteReaderAsync(ct);

            // FetchSizeの最適化
            // ExecuteReaderAsync実行により確定したRowSizeを使ってFetchSizeを最適化する
            /*
             * FetchRows (まとめて取得する行数) は100-500程度が一般的。
             * 対象のテーブルが非常に「横に長い（カラム数が多い、あるいは1カラムの定義サイズが大きい）」
             * 場合はRowSize自体が大きくなる。その際、あまり行数を大きくしすぎると
             * 1回の通信で数MBものメモリを消費し、逆にスワップが発生して遅くなる場合がある。
             * まずは100程度で試してみて、本番環境のネットワーク遅延が大きい (DBサーバーが遅い) 場合は
             * この数値を徐々に増やして調整するのがよい。
             * ベストプラクティスでは、1つのクエリにつき1MB-2MB程度のバッファを確保するのが
             * 最もコストパフォーマンスが良い (速度向上の幅が大きくメモリ負荷が低い)とされる。
             * RowSizeが小さい (例：100 bytes) 場合: FetchRows = 10000 くらいまで上げてもOK。
             * RowSizeが大きい (例：10,000 bytes) 場合: FetchRows = 100 くらいが適切。
             * 特別な制約がない限り、初期設定としては100程度が推奨。
             * デフォルト状態 (FetchSize = 65536 バイトなど) に比べて
             * ネットワーク通信回数が削減され高速化が期待できる。
             */
            reader.FetchSize = reader.RowSize * fetchRows;

            while (await reader.ReadAsync(ct))
            {
                // 1行読み込むごとに呼び出し元にyield returnする
                /*
                 * 従来の"List<TResponse>"を返す方式と"IAsyncEnumerable<TResponse>"
                 * を返す方式の最大の違いは、メモリ上でのデータの持ち方。
                 * ・List方式: 100万件のDB検索結果がある場合、100万件すべてをメモリ (List) に
                 *   格納し終わるまで、呼び出し元にはデータは一切渡されない。
                 * ・ストリーム方式: DBから1行読み込むごとに、そのデータが即座に呼び出し元へ
                 *  「配送」される。"yield return"によりこの「配送」を実現する。
                 * "yield return"はメソッドを終了せずに、「一旦この値を呼び出し元に渡し
                 * 次の要求があったら続きから再開する」という動作をする。
                 */
                yield return reader;
            }
        }
        // 1つのリクエストが終わるたびにreader.Dispose()が走り
        // 全てのリクエストが終わるとcmd.Dispose()が走る
    }

    // 同期的リソース解放
    public void Dispose()
    {
        /*
         * Connection.Close()を明示的に呼んでからDisposeすることにより
         * Oracleのセッションが即座に解放されやすくなりDB側の負担を軽減する
         */
        if (this.Connection is { State: ConnectionState.Open }) { this.Connection.Close(); }
        this.Connection?.Dispose();
        GC.SuppressFinalize(this);
    }

    // 非同期的リソース解放
    public async ValueTask DisposeAsync()
    {
        /*
         * ApiExecutor.RunAsync()内部の"await using (service)"は、対象のサービスインスタンスが
         * IAsyncDisposableインターフェースを実装していれば非同期的リソース解放（DisposeAsync）を、
         * そうでなければ同期的リソース解放（Dispose）を呼び出す
         */
        if (this.Connection is { State: ConnectionState.Open }) { await this.Connection.CloseAsync(); }
        if (this.Connection is not null) { await this.Connection.DisposeAsync(); }
        GC.SuppressFinalize(this);
    }
}
