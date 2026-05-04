using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;
using ServiceApi.Services.B1;
using System.Data;

using ServiceApi.Tests.Responses.B1;

namespace ServiceApi.Tests.Services.B1;

public class TEST_B1Service
{
    [Theory]
    [InlineData(20)]  // 存在するデータ
    [InlineData(999)] // 存在しないデータ
    public async Task ExecuteAsync_Test01(decimal deptNo)
    {
        /*
         * B1Service: ExecuteAsyncで取得した結果が、独自に取得した結果と一致すること
         * ・取得データ件数が一致すること
         * ・個々の取得データが一致すること
         */

        string connectionString = "Data Source=localhost:1521/XE;Persist Security Info=True;User ID=scott;Password=tiger";
        OracleDbExecutor ora = new(connectionString);

        //
        // 確認用データ取得
        //
        string sql =
            "SELECT EMPNO, ENAME, JOB, MGR, TO_CHAR(HIREDATE, 'yyyy/mm/dd') HIREDATE, "
            + "SAL, COMM, DEPTNO "
            + "FROM EMP "
            + "WHERE DEPTNO = :DEPTNO "
            + "ORDER BY EMPNO ";

        Action<OracleParameterCollection> bindAction = p =>
        {
            p.Add(new OracleParameter("DEPTNO", deptNo));
        };
        DataTable expectDt = ora.ExecuteQuery(sql, bindAction);

        List<B1Response> expectList = new();
        int rows = expectDt.Rows.Count;
        foreach (DataRow row in expectDt.Rows)
        {
            expectList.Add(new B1Response
            {
                EMPNO = Convert.ToDecimal(row["EMPNO"]),
                ENAME = row["ENAME"]?.ToString() ?? string.Empty,
                JOB = row["JOB"]?.ToString() ?? string.Empty,
                MGR = row["MGR"] == DBNull.Value ? null : Convert.ToDecimal(row["MGR"]),
                HIREDATE = row["HIREDATE"]?.ToString() ?? string.Empty,
                SAL = row["SAL"] == DBNull.Value ? null : Convert.ToDecimal(row["SAL"]),
                COMM = row["COMM"] == DBNull.Value ? null : Convert.ToDecimal(row["COMM"]),
                DEPTNO = row["DEPTNO"] == DBNull.Value ? null : Convert.ToDecimal(row["DEPTNO"])
            });
        }

        //
        // テスト実行
        //
        B1Service service = new(connectionString);
        B1Request request = new(){ DEPTNO = deptNo };
        List<B1Response> resultList = new();
        await foreach (var item in service.ExecuteAsync(request))
        {
            resultList.Add(item);
        }

        //
        // 結果確認
        //
        // 取得データ件数確認
        Assert.Equal(resultList.Count, expectList.Count);

        // 取得データ個別確認
        bool isMatch = true;
        foreach (B1Response result in resultList)
        {
            isMatch = expectList.Contains(result, TEST_B1ResponseComparer.Default);
            if (isMatch == false) { break; }
        }
        Assert.True(isMatch);
    }
}
