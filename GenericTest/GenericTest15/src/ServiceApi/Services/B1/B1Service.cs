using Oracle.ManagedDataAccess.Client;
using ServiceApi.Common;
using ServiceApi.Requests.B1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.B1;
using System.Data.Common;

namespace ServiceApi.Services.B1;

/*
 * API「B1」のサービスクラス
 */
public class B1Service(string connectionString, int fetchRows = ApiConstants.DefaultFetchRows)
    : ServiceBase<B1Request, B1Response>(connectionString, fetchRows)
{
    public override IAsyncEnumerable<B1Response> ExecuteAsync(
        IEnumerable<B1Request> requests,
        CancellationToken ct = default)
    {
        /*
         * SQL_B1_001:
         *   SELECT EMPNO, ENAME, JOB, MGR, TO_CHAR(HIREDATE, :SQL_DATE_FORMAT) HIREDATE, 
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
            // DATE型データはAPI内ではstringとして保持する
            // TO_CHAR()実行時に文字列フォーマットとして"SqlDateFormat"を指定する
            p.Add(new OracleParameter("SQL_DATE_FORMAT", ApiConstants.SqlDateFormat));
        };

        // マッピング用の式を定義
        Func<DbDataReader, B1Response> mapEmp = r => new B1Response 
        {
#if true
            // この記述スタイルの方が効率がよい
            EMPNO = Convert.ToDecimal(r["EMPNO"]),  // decimal - NOT NULL
            ENAME = r["ENAME"] switch
                { DBNull or null => string.Empty, var v => Convert.ToString(v)! }, // string
            JOB = r["JOB"] switch
                { DBNull or null => string.Empty,var v => Convert.ToString(v)! }, // string
            MGR = r["MGR"] switch
                { DBNull or null => null, var v => Convert.ToDecimal(v) }, // decimal
            HIREDATE = r["HIREDATE"] switch
                { DBNull or null => string.Empty, var v => Convert.ToString(v)! }, // string
            SAL = r["SAL"] switch
                { DBNull or null => null, var v => Convert.ToDecimal(v) }, // decimal
            COMM = r["COMM"] switch
                { DBNull or null => null, var v => Convert.ToDecimal(v) }, // decimal
            DEPTNO = r["DEPTNO"] switch
                { DBNull or null => null, var v => Convert.ToDecimal(v) }  // decimal
                /*
                 * あるいは以下のような「拡張メソッド」を定義し、
                 * 
                 * public static class DbDataReaderExtensions
                 * {
                 *     public static string GetStringOrEmpty(this DbDataReader r, string columnName)
                 *         => r[columnName] switch { DBNull or null => string.Empty, var v => Convert.ToString(v)! };
                 *     public static decimal? GetDecimalOrNull(this DbDataReader r, string columnName)
                 *         => r[columnName] switch { DBNull or null => null, var v => Convert.ToDecimal(v) };
                 * }
                 * 
                 * これを以下のように呼び出すのも可
                 * 
                 * Func<DbDataReader, B1Response> mapFunc = r => new B1Response 
                 * {
                 *     EMPNO = Convert.ToDecimal(r["EMPNO"]), 
                 *     ENAME    = r.GetStringOrEmpty("ENAME"),
                 *     JOB      = r.GetStringOrEmpty("JOB"),
                 *     HIREDATE = r.GetStringOrEmpty("HIREDATE"),
                 *     MGR      = r.GetDecimalOrNull("MGR"),
                 *     SAL      = r.GetDecimalOrNull("SAL"),
                 *     COMM     = r.GetDecimalOrNull("COMM"),
                 *     DEPTNO   = r.GetDecimalOrNull("DEPTNO")
                 * };
                 * 
                 * 拡張メソッドを使用するためのルール:
                 * C#で拡張メソッドを使用するには、以下の条件を満たす必要がある
                 *   1. クラスがstaticであること
                 *   2. メソッドがstaticであること
                 *   3. 第1引数にthisを付けること
                 * この条件が揃うことで、コンパイラは
                 * 「DbDataReader型のオブジェクトであれば、このメソッドをドット(.)で呼び出してよい」
                 * という許可を与える
                 */
#else
            EMPNO = Convert.ToDecimal(r["EMPNO"]), // decimal - NOT NULL
            ENAME = Convert.ToString(r["ENAME"]) ?? string.Empty,  // string
            JOB = Convert.ToString(r["JOB"]) ?? string.Empty,
            MGR = r["MGR"] is DBNull ? null : Convert.ToDecimal(r["MGR"]), // decimal
            HIREDATE = Convert.ToString(r["HIREDATE"]) ?? string.Empty,
            SAL = r["SAL"] is DBNull ? null : Convert.ToDecimal(r["SAL"]),
            COMM = r["COMM"] is DBNull ? null : Convert.ToDecimal(r["COMM"]),
            DEPTNO = r["DEPTNO"] is DBNull ? null : Convert.ToDecimal(r["DEPTNO"])
#endif
        };

        /*
         * ここにはasync/awaitキーワードは不要
         * ExecuteQueryAsync（基底クラス側）が非同期ストリームの実体を作成して返却するため
         * 具象クラスは単なる「パス（中継役）」として振る舞えばよい
         */
        return ExecuteQueryAsync(sql, requests, bindAction, mapEmp, ct);
    }
}
