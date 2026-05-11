using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests.C1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.C1;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace ServiceApi.Services.C1;

/*
 * API「C1」のサービスクラス
 */
public class C1Service(string connectionString)
    : ServiceBase<C1Request, C1Response>(connectionString)
{
    public override async IAsyncEnumerable<C1Response> ExecuteAsync(
        IEnumerable<C1Request> requests,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        /*
         * SQL_C1_001:
         *   SELECT d.DEPTNO, d.DNAME, e.EMPNO, e.ENAME
         *   FROM DEPT d
         *   INNER JOIN EMP e
         *   ON e.DEPTNO = d.DEPTNO
         *   WHERE d.DEPTNO = :DEPTNO
         *   ORDER BY d.DEPTNO, e.EMPNO
         */
        string sql = SqlResourceProvider.GetSql(SqlId.SQL_C1_001);

        // パラメータ設定用の式を定義
        Action<OracleParameterCollection, C1Request> bindAction = (p, req) =>
        {
            p.Add(new OracleParameter("DEPTNO", req.DEPTNO));
        };

        // Empマッピング定義
        C1Response.Emp empMapFunc(DbDataReader r) => new()
        {
            EMPNO = Convert.ToDecimal(r["EMPNO"]),
            ENAME = Convert.ToString(r["ENAME"]) ?? string.Empty
        };

        foreach (var request in requests)
        {
            C1Response? response = null;

            // 1つのリクエスト（1つのSQL実行結果）を処理
            // このExecuteQueryAsyncは単一のリクエストを配列化して渡す
            await foreach (var reader in ExecuteQueryAsync(sql, [request], bindAction, ct))
            {
                decimal deptNo = Convert.ToDecimal(reader["DEPTNO"]);

                if (response is null || response.DEPTNO != deptNo)
                {
                    if (response is not null)
                    {
                        // 作成済のオブジェクトを返却
                        yield return response;
                        //await Task.Delay(2000); // テスト用
                    }

                    // 新しいオブジェクトを作成
                    response = new C1Response
                    {
                        DEPTNO = deptNo,
                        DNAME = Convert.ToString(reader["DNAME"]) ?? string.Empty,
                        Employees = [empMapFunc(reader)]
                    };
                }
                else
                {
                    // DEPTNOが同一の場合はempMapFuncを使ってリストに追加
                    response.Employees.Add(empMapFunc(reader));
                }
            }

            // リクエスト1件分のSQL実行が終わったら、残っているresponseを返却
            if (response is not null) { yield return response; }
        }
    }
}
