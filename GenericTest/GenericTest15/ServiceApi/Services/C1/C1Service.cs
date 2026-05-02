using ServiceApi.Requests.C1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.C1;
using System.Data.Common;

namespace ServiceApi.Services.C1;

public class C1Service(string connectionString)
    : ServiceBase<C1Request, C1Response>(connectionString), IC1Service
{
    public override async IAsyncEnumerable<C1Response> ExecuteAsync(C1Request request)
    {
        /*
         * 1. **データの集約**: SQLの結果（フラットな行）を1行ずつ読み込む。
         * 2. **親のインスタンス化**: `DEPTNO` が変わったら新しい `DeptResponse` を作成する。
         * 3. **子の追加**: 同じ `DEPTNO` の間は、その `DeptResponse.Employees` リストに対して `new EmpResponse { ... }` を `Add` し続ける。
         * 4. **ストリーミング送出**: 次の `DEPTNO` に移る直前、完成した `DeptResponse` を `yield return` する。
         */

        /*
         * SQL_C1_001:
         *   SELECT d.DEPTNO, d.DNAME, e.EMPNO, e.ENAME
         *   FROM DEPT d
         *   INNER JOIN EMP e
         *   ON e.DEPTNO = d.DEPTNO
         *   ORDER BY d.DEPTNO, e.EMPNO
         */
        string sql = SqlResource.GetSql(SqlId.SQL_C1_001);

        // --- Employeesマッピングロジックを定義 ---
        C1Response.Emp mapFunc(DbDataReader reader) => new()
        {
            EMPNO = reader.GetDecimal(reader.GetOrdinal("EMPNO")),
            ENAME = reader.IsDBNull(reader.GetOrdinal("ENAME"))
                ? string.Empty : reader.GetString(reader.GetOrdinal("ENAME"))
        };

        /*
         * DEPTNOが同一のレコードをグループ化して返却する
         */
        C1Response? response = null;
        await foreach (var r in ExecuteQueryAsync(sql))
        {
            decimal deptNo = r.GetDecimal(r.GetOrdinal("DEPTNO"));
            if (response == null || response.DEPTNO != deptNo)
            {
                if (response != null) { 
                    yield return response;
                    //await Task.Delay(2000); // テスト用
                }

                // 新しいC1Responseを作成する
                response = new C1Response
                {
                    DEPTNO = deptNo,
                    DNAME = r.IsDBNull(r.GetOrdinal("DNAME"))
                        ? string.Empty : r.GetString(r.GetOrdinal("DNAME")),
                    Employees = [mapFunc(r)]
                };
            } else
            {
                // DEPTNOが同一の場合は、MapEmp を使ってリストに追加
                response.Employees.Add(mapFunc(r));
            }
        }
        /* 最後のオブジェクトを返却 */
        if (response != null) { yield return response; }
    }
}
