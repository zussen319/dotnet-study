using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests.A1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.A1;

namespace ServiceApi.Services.A1;

public class A1Service(string connectionString)
    : ServiceBase<A1Request, A1Response>(connectionString), IA1Service
{
#if true
    public override IAsyncEnumerable<A1Response> ExecuteAsync(A1Request request) =>
        ExecuteQueryAsync(
            // 実行するSQLIDとパラメータ設定用のラムダ式を渡す
            /*
             * async/awaitキーワードは不要
             * ExecuteQueryAsync（基底クラス側）が非同期ストリームの実態を作成して返してくれるので
             * 具象クラス（A1Service）は単なる「パス（中継役）」として振る舞えばよい
             */
            SqlId.SQL_A1_001,
            p =>
            {
                p.Add(new OracleParameter("VAL", request.A1Value));
            });
#else
    public override async IAsyncEnumerable<A1Response> ExecuteAsync(A1Request request)
    {
        // 1. コマンドの作成とバインド
        string sql = SqlResource.GetSql(SqlId.SQL_A1_001);
        var cmd = new OracleCommand(sql);

        // 2. パラメータのバインド
        cmd.Parameters.Add(new OracleParameter("VAL", request.A1Value));

        // 3. 基底クラスの ExecuteQueryAsync を呼び出す
        // await foreach を使って、基底クラスから流れてくるデータを順次 yield return する
        /*
         * ## 2. 非同期のまま「反復（foreach）」を維持する
         * 通常、"foreach" は同期的な処理ですが、DBアクセスのような待ち時間が発生するばあ、
         * 従来の "foreach" ではスレッドが止まってしまいます。
         * 今回のポイントは、**"await foreach"** を使っている点です。
         * "A1Service" や "ApiExecutor" が "await foreach" で待ち受けます。
         * データが届くまではスレッドを解放（非同期待機）し、データが届いた瞬間だけ処理を動かすという
         * 「非同期」と「繰り返し処理」の融合が実現されています。
         */
        await foreach(var response in ExeuteQueyAsync(cmd))
        {
            yield return response;
        }
    }
#endif
}
