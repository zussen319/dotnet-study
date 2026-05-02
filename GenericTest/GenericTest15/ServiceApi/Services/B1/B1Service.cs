using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests.B1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.B1;
using System.Data.Common;

namespace ServiceApi.Services.B1;

public class B1Service(string connectionString)
    : ServiceBase<B1Request, B1Response>(connectionString), IB1Service
{
    public override IAsyncEnumerable<B1Response> ExecuteAsync(B1Request request)
    {
        /*
         * SQL_B1_001:
         *   SELECT EMPNO, ENAME, JOB, MGR, TO_CHAR(HIREDATE, 'yyyy/mm/dd') HIREDATE, 
         *          SAL, COMM, DEPTNO 
         *   FROM EMP 
         *   WHERE DEPTNO = :DEPTNO 
         *   ORDER BY EMPNO
         */
        string sql = SqlResource.GetSql(SqlId.SQL_B1_001);
          
        // パラメータ設定用の式を定義 (引数：OracleParameterCollection, 戻り値：なし)
        Action<OracleParameterCollection> bindAction = p => 
        {
            p.Add(new OracleParameter("DEPTNO", request.DEPTNO));
        };

        // マッピング用の式を定義 (引数：DbDataReader, 戻り値：B1Response)
        Func<DbDataReader, B1Response> mapFunc = r => new B1Response 
        {
            EMPNO = r.GetDecimal(r.GetOrdinal("EMPNO")), // NOT NULL
            ENAME = r.IsDBNull(r.GetOrdinal("ENAME"))
                ? string.Empty : r.GetString(r.GetOrdinal("ENAME")),
            JOB = r.IsDBNull(r.GetOrdinal("JOB"))
                ? string.Empty : r.GetString(r.GetOrdinal("JOB")),
            MGR = r.IsDBNull(r.GetOrdinal("MGR"))
                ? null : r.GetDecimal(r.GetOrdinal("MGR")),
            HIREDATE = r.IsDBNull(r.GetOrdinal("HIREDATE"))
                ? string.Empty : r.GetString(r.GetOrdinal("HIREDATE")),
            SAL = r.IsDBNull(r.GetOrdinal("SAL"))
                ? null : r.GetDecimal(r.GetOrdinal("SAL")),
            COMM = r.IsDBNull(r.GetOrdinal("COMM"))
                ? null : r.GetDecimal(r.GetOrdinal("COMM")),
            DEPTNO = r.IsDBNull(r.GetOrdinal("DEPTNO"))
                ? null : r.GetDecimal(r.GetOrdinal("DEPTNO"))
        };

        /*
         * async/awaitキーワードは不要
         * ExecuteQueryAsync（基底クラス側）が非同期ストリームの実体を作成して返してくれるので
         * 具象クラス（A1Service）は単なる「パス（中継役）」として振る舞えばよい
         */
        return ExecuteQueryAsync(sql, bindAction, mapFunc);
    }
}
