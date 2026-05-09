using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests.A1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.A1;
using System.Data.Common;

namespace ServiceApi.Services.A1;

public class A1Service(string connectionString)
    : ServiceBase<A1Request, A1Response>(connectionString) /*, IA1Service */
{
    public override IAsyncEnumerable<A1Response> ExecuteAsync(
        IEnumerable<A1Request> requests,
        CancellationToken ct = default)
    {
        string sql = SqlResourceProvider.GetSql(SqlId.SQL_A1_001);
          
        // パラメータ設定用の式を定義
        Action<OracleParameterCollection, A1Request> bindAction = (p, req) => 
        {
            p.Add(new OracleParameter("VAL", req.A1Value));
        };

        // マッピング用の式を定義
        Func<DbDataReader, A1Response> mapFunc = r => new A1Response 
        {
            Id = Convert.ToDecimal(r["ID"]),
            DataName = Convert.ToString(r["DATANAME"]) ?? string.Empty,
        };

        /*
         * async/awaitキーワードは不要
         * ExecuteQueryAsync（基底クラス側）が非同期ストリームの実体を作成して返してくれるので
         * 具象クラス（A1Service）は単なる「パス（中継役）」として振る舞えばよい
         */
        return ExecuteQueryAsync(sql, requests, bindAction, mapFunc, ct);
    }
}
