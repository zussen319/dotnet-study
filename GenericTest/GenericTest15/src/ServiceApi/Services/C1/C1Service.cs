#if true
using Oracle.ManagedDataAccess.Client;
using ServiceApi.Common;
using ServiceApi.Requests.C1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.C1;
using System.Data.Common;

namespace ServiceApi.Services.C1;

/*
 * API「C1」のサービスクラス
 */
public class C1Service(string connectionString, int fetchRows = ApiConstants.DefaultFetchRows)
    : ServiceBase<C1Request, C1Response>(connectionString, fetchRows) {
    public override IAsyncEnumerable<C1Response> ExecuteAsync(
        IEnumerable<C1Request> requests,
        CancellationToken ct = default)
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

        // パラメータ設定用の式を定義（型明示）
        Action<OracleParameterCollection, C1Request> bindAction = (p, req) => {
            p.Add(new OracleParameter("DEPTNO", req.DEPTNO));
        };

        // 従業員（Emp）1行分のマッピング（map：1レコード→1オブジェクト）
        // ExecuteAsync内でのみ使用するためローカル関数とする
        C1Response.Emp mapEmp(DbDataReader r) => new() {
            EMPNO = Convert.ToDecimal(r["EMPNO"]),
            ENAME = Convert.ToString(r["ENAME"]) ?? string.Empty
        };

        /*
         * group：複数レコード→1オブジェクト
         * 1リクエスト分の行ストリームを受け取り、DEPTNOでグルーピングして返す
         * コマンド生成・再バインド・カーソル再利用・FetchSize最適化は基底が担うので、
         * ここは集約ロジックだけに集中する
         * （mapEmpを参照するためstaticにはしない／asyncイテレータなので必然的に型付きローカル関数）
         */
        async IAsyncEnumerable<C1Response> groupDept(IAsyncEnumerable<DbDataReader> rows)
        {
            C1Response? dept = null;

            await foreach (DbDataReader reader in rows) {
                decimal deptNo = Convert.ToDecimal(reader["DEPTNO"]);

                if (dept is null || dept.DEPTNO != deptNo) {
                    // 先頭行、または次のDEPTNOの場合
                    // 直前のオブジェクトを確定して返す
                    if (dept is not null) { yield return dept; }

                    // 新しいオブジェクトを作成
                    dept = new C1Response {
                        DEPTNO = deptNo,
                        DNAME = Convert.ToString(reader["DNAME"]) ?? string.Empty,
                        Employees = [mapEmp(reader)]
                    };
                } else {
                    // 同一DEPTNOの場合：Employeesリストに追加
                    dept.Employees.Add(mapEmp(reader));
                }
            }

            // 末尾に残ったオブジェクトを返す
            if (dept is not null) { yield return dept; }
        }

        // ExecuteQueryAsync（overload(4): bindAction + groupFunc）
        return ExecuteQueryAsync(sql, requests, bindAction, groupDept, ct);
    }
}
#else
using Oracle.ManagedDataAccess.Client;
using ServiceApi.Common;
using ServiceApi.Requests.C1;
using ServiceApi.Resources.Sql;
using ServiceApi.Responses.C1;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace ServiceApi.Services.C1;

/*
 * API「C1」のサービスクラス
 */
public class C1Service(string connectionString, int fetchRows = ApiConstants.DefaultFetchRows)
    : ServiceBase<C1Request, C1Response>(connectionString, fetchRows)
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

        foreach (C1Request request in requests)
        {
            C1Response? response = null;

            // 1つのリクエスト（1つのSQL実行結果）を処理
            // このExecuteQueryAsyncは単一のリクエストを配列化して渡す
            await foreach (DbDataReader reader in ExecuteQueryAsync(sql, [request], bindAction, ct))
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
#endif
