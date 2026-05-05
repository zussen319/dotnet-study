using ServiceApi.Requests.C1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.C1;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace ServiceApi.Services.C1;

public class C1Service(string connectionString)
    : ServiceBase<C1Request, C1Response>(connectionString), IC1Service
{
    public override async IAsyncEnumerable<C1Response> ExecuteAsync(
        C1Request request,
        [EnumeratorCancellation] CancellationToken ct = default)
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
        string sql = SqlResourceProvider.GetSql(SqlId.SQL_C1_001);

        // Empマッピング定義
        C1Response.Emp empMapFunc(DbDataReader r) => new()
        {
            EMPNO = r.GetDecimal(r.GetOrdinal("EMPNO")),
            ENAME = r.IsDBNull(r.GetOrdinal("ENAME"))
                ? string.Empty : r.GetString(r.GetOrdinal("ENAME"))
        };

        /*
         * DEPTNOが同一のレコードをグループ化して返却する
         */
        C1Response? response = null;
        await foreach (var reader in ExecuteQueryAsync(sql, ct))
        {
            decimal deptNo = reader.GetDecimal(reader.GetOrdinal("DEPTNO"));
            if (response == null || response.DEPTNO != deptNo)
            {
                if (response != null) {
                    // 作成済のオブジェクトを返却
                    yield return response;
                    //await Task.Delay(2000); // テスト用
                }

                // 新しいオブジェクトを作成
                response = new C1Response
                {
                    DEPTNO = deptNo,
                    DNAME = reader.IsDBNull(reader.GetOrdinal("DNAME"))
                        ? string.Empty : reader.GetString(reader.GetOrdinal("DNAME")),
                    Employees = [empMapFunc(reader)]
                };
            } else
            {
                // DEPTNOが同一の場合は、MapEmp を使ってリストに追加
                response.Employees.Add(empMapFunc(reader));
            }
        }
        // 最後のオブジェクトを返却
        if (response != null) { yield return response; }
    }
}
