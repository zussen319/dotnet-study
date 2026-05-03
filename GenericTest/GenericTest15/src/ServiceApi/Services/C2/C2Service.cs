using ServiceApi.Requests.C2;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.C2;
using System.Data.Common;

namespace ServiceApi.Services.C2;

public class C2Service(string connectionString)
    : ServiceBase<C2Request, C2Response>(connectionString), IC2Service
{
    public override async IAsyncEnumerable<C2Response> ExecuteAsync(C2Request request)
    {
        /*
         * 1. **データの集約**: SQLの結果（フラットな行）を1行ずつ読み込む。
         * 2. **親のインスタンス化**: `DEPTNO` が変わったら新しい `DeptResponse` を作成する。
         * 3. **子の追加**: 同じ `DEPTNO` の間は、その `DeptResponse.Employees` リストに対して `new EmpResponse { ... }` を `Add` し続ける。
         * 4. **ストリーミング送出**: 次の `DEPTNO` に移る直前、完成した `DeptResponse` を `yield return` する。
         */

        /*
         * SQL_C2_001:
         *   SELECT d.DEPTNO DEPTNO, d.DNAME DNAME,               -- レベル１
         *          e1.EMPNO MEMBER_EMPNO, e1.ENAME MEMBER_ENAME, -- レベル２
         *          e2.EMPNO STAFF_EMPNO, e2.ENAME STAFF_ENAME    -- レベル３
         *   FROM DEPT d 
         *   INNER JOIN EMP e1 ON e1.DEPTNO = d.DEPTNO 
         *   INNER JOIN EMP e2 ON e2.MGR = e1.EMPNO 
         *   ORDER BY d.DEPTNO, e1.EMPNO, e2.EMPNO
         */
        string sql = SqlResourceProvider.GetSql(SqlId.SQL_C2_001);

        // レベル２：Memberマッピング定義
        C2Response.Member memberMapFunc(DbDataReader r) => new()
        {
            MEMBER_EMPNO = r.GetDecimal(r.GetOrdinal("MEMBER_EMPNO")),
            MEMBER_ENAME = r.IsDBNull(r.GetOrdinal("MEMBER_ENAME"))
                ? string.Empty : r.GetString(r.GetOrdinal("MEMBER_ENAME")),
            Staffs = [staffMapFunc(r)]
        };

        // レベル３：Staffマッピング定義
        C2Response.Staff staffMapFunc(DbDataReader r) => new()
        {
            STAFF_EMPNO = r.GetDecimal(r.GetOrdinal("STAFF_EMPNO")),
            STAFF_ENAME = r.IsDBNull(r.GetOrdinal("STAFF_ENAME"))
                ? string.Empty : r.GetString(r.GetOrdinal("STAFF_ENAME"))
        };

        /*
         * DEPTNOが同一のレコードをグループ化して返却する
         */
        C2Response? dept = null;
        C2Response.Member? member = null;
        await foreach (var reader in ExecuteQueryAsync(sql))
        {
            decimal deptNo = reader.GetDecimal(reader.GetOrdinal("DEPTNO"));
            decimal memberEmpNo = reader.GetDecimal(reader.GetOrdinal("MEMBER_EMPNO"));

            if (dept == null || dept.DEPTNO != deptNo)
            {
                // レベル１：先頭レコード、またはDEPTNOが不一致の場合
                if (dept != null)
                {
                    // 作成済のオブジェクトを返却
                    yield return dept;
                    //await Task.Delay(2000); // テスト用
                }

                // 新しいオブジェクトを作成
                dept = new C2Response
                {
                    DEPTNO = deptNo,
                    DNAME = reader.IsDBNull(reader.GetOrdinal("DNAME"))
                        ? string.Empty : reader.GetString(reader.GetOrdinal("DNAME")),
                    Members = [(member = memberMapFunc(reader))]
                };
            }
            else if (member?.MEMBER_EMPNO != memberEmpNo)
            {
                // レベル２：MEMBER_EMPNOが不一致の場合
                dept.Members.Add((member = memberMapFunc(reader)));
            }
            else
            {
                // レベル３：その他
                member?.Staffs.Add(staffMapFunc(reader));
            }
        }
        // 最後のオブジェクトを返却
        if (dept != null) { yield return dept; }
    }
}
