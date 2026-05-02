using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests.A1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.A1;
using System.Data.Common;

namespace ServiceApi.Services.A1;

public class A1Service(string connectionString)
    : ServiceBase<A1Request, A1Response>(connectionString), IA1Service
{
    public override IAsyncEnumerable<A1Response> ExecuteAsync(A1Request request)
    {
        string sql = SqlResourceProvider.GetSql(SqlId.SQL_A1_001);
          
        // パラメータ設定用の式を定義 (引数：OracleParameterCollection, 戻り値：なし)
        Action<OracleParameterCollection> bindAction = p => 
        {
            p.Add(new OracleParameter("VAL", request.A1Value));
        };

        // マッピング用の式を定義 (引数：DbDataReader, 戻り値：A1Response)
        Func<DbDataReader, A1Response> mapFunc = r => new A1Response 
        {
            Id = r.GetDecimal(r.GetOrdinal("ID")),
            DataName = r.IsDBNull(r.GetOrdinal("DATANAME"))
                ? string.Empty
                : r.GetString(r.GetOrdinal("DATANAME")),
        };

        /*
         * async/awaitキーワードは不要
         * ExecuteQueryAsync（基底クラス側）が非同期ストリームの実体を作成して返してくれるので
         * 具象クラス（A1Service）は単なる「パス（中継役）」として振る舞えばよい
         */
        return ExecuteQueryAsync(sql, bindAction, mapFunc);
    }
}
