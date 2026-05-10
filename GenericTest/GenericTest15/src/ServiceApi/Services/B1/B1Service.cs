using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests.B1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.B1;
using System.Data.Common;

namespace ServiceApi.Services.B1;

public class B1Service(string connectionString)
    : ServiceBase<B1Request, B1Response>(connectionString)
{
    public override IAsyncEnumerable<B1Response> ExecuteAsync(
        IEnumerable<B1Request> requests,
        CancellationToken ct = default)
    {
        /*
         * SQL_B1_001:
         *   SELECT EMPNO, ENAME, JOB, MGR, TO_CHAR(HIREDATE, 'yyyy/mm/dd') HIREDATE, 
         *          SAL, COMM, DEPTNO 
         *   FROM EMP 
         *   WHERE DEPTNO = :DEPTNO 
         *   ORDER BY EMPNO
         */
        string sql = SqlResourceProvider.GetSql(SqlId.SQL_B1_001);
          
        // パラメータ設定用の式を定義
        Action<OracleParameterCollection, B1Request> bindAction = (p, req) => 
        {
            p.Add(new OracleParameter("DEPTNO", req.DEPTNO));
        };

        // マッピング用の式を定義
        Func<DbDataReader, B1Response> mapFunc = r => new B1Response 
        {
            EMPNO = Convert.ToDecimal(r["EMPNO"]), // decimal - NOT NULL
            ENAME = Convert.ToString(r["ENAME"]) ?? string.Empty,  // string
            JOB = Convert.ToString(r["JOB"]) ?? string.Empty,
            MGR = r["MGR"] is DBNull ? null : Convert.ToDecimal(r["MGR"]), // decimal
            HIREDATE = Convert.ToString(r["HIREDATE"]) ?? string.Empty,
            SAL = r["SAL"] is DBNull ? null : Convert.ToDecimal(r["SAL"]),
            COMM = r["COMM"] is DBNull ? null : Convert.ToDecimal(r["COMM"]),
            DEPTNO = r["DEPTNO"] is DBNull ? null : Convert.ToDecimal(r["DEPTNO"])
        };

        /*
         * async/awaitキーワードは不要
         * ExecuteQueryAsync（基底クラス側）が非同期ストリームの実体を作成して返却するため
         * 具象クラスは単なる「パス（中継役）」として振る舞えばよい
         */
        return ExecuteQueryAsync(sql, requests, bindAction, mapFunc, ct);
    }
}
