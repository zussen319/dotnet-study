using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests.C2;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.C2;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace ServiceApi.Services.C2;

/// <summary>
/// サービスクラス（C2）
/// </summary>
/// <param name="connectionString">DB接続文字列</param>
public class C2Service(string connectionString)
    : ServiceBase<C2Request, C2Response>(connectionString)
{
    /// <summary>
    /// サービスエントリポイント
    /// </summary>
    /// <param name="requests">リクエスト配列</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns></returns>
    public override async IAsyncEnumerable<C2Response> ExecuteAsync(
        IEnumerable<C2Request> requests,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        /*
         * SQL_C2_001:
         *   SELECT d.DEPTNO DEPTNO, d.DNAME DNAME,               -- レベル１
         *          e1.EMPNO MEMBER_EMPNO, e1.ENAME MEMBER_ENAME, -- レベル２
         *          e2.EMPNO STAFF_EMPNO, e2.ENAME STAFF_ENAME    -- レベル３
         *   FROM DEPT d 
         *   INNER JOIN EMP e1 ON e1.DEPTNO = d.DEPTNO 
         *   INNER JOIN EMP e2 ON e2.MGR = e1.EMPNO 
         *   WHERE d.DEPTNO = :DEPTNO
         *   ORDER BY d.DEPTNO, e1.EMPNO, e2.EMPNO
         */
        string sql = SqlResourceProvider.GetSql(SqlId.SQL_C2_001);

        // パラメータ設定用の式を定義
        Action<OracleParameterCollection, C2Request> bindAction = (p, req) =>
        {
            p.Add(new OracleParameter("DEPTNO", req.DEPTNO));
        };

        // レベル２：Memberマッピング定義
        C2Response.Member memberMapFunc(DbDataReader r) => new()
        {
            MEMBER_EMPNO = Convert.ToDecimal(r["MEMBER_EMPNO"]), // decimal - NOT NULL
            MEMBER_ENAME = Convert.ToString(r["MEMBER_ENAME"]) ?? string.Empty,  // string
            Staffs = [staffMapFunc(r)]
        };

        // レベル３：Staffマッピング定義
        C2Response.Staff staffMapFunc(DbDataReader r) => new()
        {
            STAFF_EMPNO = Convert.ToDecimal(r["STAFF_EMPNO"]), // decimal - NOT NULL
            STAFF_ENAME = Convert.ToString(r["STAFF_ENAME"]) ?? string.Empty  // string
        };

        /*
         * DEPTNOが同一のレコードをグループ化して返却する
         */
        foreach (var req in requests)
        {
            C2Response? dept = null;
            C2Response.Member? member = null;

            await foreach (var reader in ExecuteQueryAsync(sql, [req], bindAction, ct))
            {
                decimal deptNo = Convert.ToDecimal(reader["DEPTNO"]); // decimal - NOT NULL
                decimal memberEmpNo = Convert.ToDecimal(reader["MEMBER_EMPNO"]); // decimal - NOT NULL

                if (dept is null || dept.DEPTNO != deptNo)
                {
                    // レベル１：先頭レコード、またはDEPTNOが不一致の場合
                    if (dept is not null)
                    {
                        // 作成済のオブジェクトを返却
                        yield return dept;
                        //await Task.Delay(2000); // テスト用
                    }

                    // 新しいオブジェクトを作成
                    dept = new C2Response
                    {
                        DEPTNO = deptNo,
                        DNAME = Convert.ToString(reader["DNAME"]) ?? string.Empty,
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

            // リクエスト1件分のSQL実行が終わったら、残っているresponseを返却
            if (dept is not null) { yield return dept; }
        }
    }
}
