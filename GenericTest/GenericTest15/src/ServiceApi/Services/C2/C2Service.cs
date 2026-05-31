#if true
using Oracle.ManagedDataAccess.Client;
using ServiceApi.Common;
using ServiceApi.Requests.C2;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.C2;
using System.Data.Common;

namespace ServiceApi.Services.C2;

/*
 * API「C2」のサービスクラス
 */
public class C2Service(string connectionString, int fetchRows = ApiConstants.DefaultFetchRows)
    : ServiceBase<C2Request, C2Response>(connectionString, fetchRows) {
    public override IAsyncEnumerable<C2Response> ExecuteAsync(
        IEnumerable<C2Request> requests,
        CancellationToken ct = default)
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

        // パラメータ設定用の式を定義（型明示）
        Action<OracleParameterCollection, C2Request> bindAction = (p, req) => {
            p.Add(new OracleParameter("DEPTNO", req.DEPTNO));
        };

        // レベル３：Staff 1行分のマッピング（map：1レコード→1オブジェクト）
        C2Response.Staff mapStaff(DbDataReader r) => new() {
            STAFF_EMPNO = Convert.ToDecimal(r["STAFF_EMPNO"]),
            STAFF_ENAME = Convert.ToString(r["STAFF_ENAME"]) ?? string.Empty
        };

        // レベル２：Member 1行分のマッピング（先頭Staffも同時に確定）
        C2Response.Member mapMember(DbDataReader r) => new() {
            MEMBER_EMPNO = Convert.ToDecimal(r["MEMBER_EMPNO"]),
            MEMBER_ENAME = Convert.ToString(r["MEMBER_ENAME"]) ?? string.Empty,
            Staffs = [mapStaff(r)]
        };

        /*
         * group：複数レコード→1オブジェクト
         * 1リクエスト分の行ストリームを DEPTNO→MEMBER_EMPNO の3階層ブレイクで集約する。
         * （mapMember/mapStaffを参照するためstaticにはしない）
         */
        async IAsyncEnumerable<C2Response> groupDept(IAsyncEnumerable<DbDataReader> rows)
        {
            C2Response? dept = null;
            C2Response.Member? member = null;

            await foreach (DbDataReader reader in rows) {
                decimal deptNo = Convert.ToDecimal(reader["DEPTNO"]);          // NOT NULL
                decimal memberEmpNo = Convert.ToDecimal(reader["MEMBER_EMPNO"]); // NOT NULL

                if (dept is null || dept.DEPTNO != deptNo) {
                    // レベル１：先頭レコード、またはDEPTNOブレイク
                    if (dept is not null) { yield return dept; }

                    // 新しいDeptを作成（先頭Memberもこの時点で確定）
                    dept = new C2Response {
                        DEPTNO = deptNo,
                        DNAME = Convert.ToString(reader["DNAME"]) ?? string.Empty,
                        Members = [(member = mapMember(reader))]
                    };
                } else if (member?.MEMBER_EMPNO != memberEmpNo) {
                    // レベル２：同一Dept内でMEMBER_EMPNOがブレイク
                    dept.Members.Add((member = mapMember(reader)));
                } else {
                    // レベル３：同一Member配下のStaffを追加
                    member?.Staffs.Add(mapStaff(reader));
                }
            }

            // 末尾に残ったDeptを返す
            if (dept is not null) { yield return dept; }
        }

        // ExecuteQueryAsync（overload(4): bindAction + groupFunc）
        return ExecuteQueryAsync(sql, requests, bindAction, groupDept, ct);
    }
}
#else
using Oracle.ManagedDataAccess.Client;
using ServiceApi.Common;
using ServiceApi.Requests.C2;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.C2;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace ServiceApi.Services.C2;

/*
 * API「C2」のサービスクラス
 */
public class C2Service(string connectionString, int fetchRows = ApiConstants.DefaultFetchRows)
    : ServiceBase<C2Request, C2Response>(connectionString, fetchRows)
{
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
         * DEPTNOが同一のレコードをグループ化して返却
         */
        foreach (C2Request request in requests)
        {
            C2Response? dept = null;
            C2Response.Member? member = null;

            await foreach (DbDataReader reader in ExecuteQueryAsync(sql, [request], bindAction, ct))
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
#endif
