using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;
using ServiceApi.Services.B1;
using ServiceApi.Tests.Common;
using ServiceApi.Tests.Responses.B1;
using System.Data;
using System.Data.Common;

namespace ServiceApi.Tests.Services.B1;

public class TEST_B1Service
{
    private const string _connectionString =
        "Data Source=localhost:1521/XE;Persist Security Info=True;User ID=scott;Password=tiger";

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
        TEST_DbManipulator dbm = new(_connectionString);

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
#if false
        Func<DbDataReader, B1Response> mapFunc = r => new B1Response
        {
#if true
            EMPNO = r.GetDecimal(r.GetOrdinal("EMPNO")),
            ENAME = r.GetValue(r.GetOrdinal("ENAME")) as string ?? string.Empty,
            JOB = r.GetValue(r.GetOrdinal("JOB")) as string ?? string.Empty,
            MGR = r.GetValue(r.GetOrdinal("MGR")) as decimal?,
            HIREDATE = r.GetValue(r.GetOrdinal("HIREDATE")) as string ?? string.Empty,
            SAL = r.GetValue(r.GetOrdinal("SAL")) as decimal?,
            COMM = r.GetValue(r.GetOrdinal("COMM")) as decimal?,
            DEPTNO = r.GetValue(r.GetOrdinal("DEPTNO")) as decimal?
#else
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
#endif
        };
        List<B1Response> expectList = [];
        await foreach (var item in dbm.ExecuteQueryAsync<B1Response>(sql, bindAction, mapFunc))
        {
            expectList.Add(item);
        }
#else
        DataTable expectDt = dbm.ExecuteQuery(sql, bindAction);

        List<B1Response> expectList = [];
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
#endif

        //
        // テスト実行
        //
        B1Service service = new(_connectionString);
        B1Request request = new(){ DEPTNO = deptNo };
        List<B1Response> resultList = [];
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

#if true
    /*
     * これらはExecuteAsync_Test01により確認できているため不要と判断
     */
    [Theory]
    [InlineData(10, 3)] // 部門10には3件のデータがある想定
    [InlineData(20, 5)] // 部門20には5件のデータがある想定
    public async Task ExecuteAsync_ShouldReturnDataFromDatabase(decimal deptNo, int expectedCount)
    {
        // Arrange
        var service = new B1Service(_connectionString);
        var request = new B1Request { DEPTNO = deptNo };
        var results = new List<B1Response>();

        // Act
        await foreach (var item in service.ExecuteAsync(request))
        {
            results.Add(item);
        }

        // Assert
        Assert.Equal(expectedCount, results.Count);

        // データの整合性チェック（例：最初の1件のEMPNOが0でない等）
        Assert.All(results, r => Assert.True(r.EMPNO > 0));
        Assert.All(results, r => Assert.NotNull(r.ENAME));
    }

    /*
     * B1ServiceとB1Service_Testの結果を比較する
     * （B1Service_Test.jsonで読み込むデータには、実テーブルの全データを登録しておく）
     */
    [Theory]
    [InlineData(10)]
    public async Task Compare_RealService_With_TestService(decimal deptNo)
    {
        // Arrange
        var realService = new B1Service(_connectionString);
        var testService = new B1Service_Test(_connectionString);
        var request = new B1Request { DEPTNO = deptNo };

        var realList = new List<B1Response>();
        var testList = new List<B1Response>();

        // Act
        await foreach (var r in realService.ExecuteAsync(request)) realList.Add(r);
        await foreach (var t in testService.ExecuteAsync(request)) testList.Add(t);

        // testListにはJsonファイルの全データがロードされるため
        // request.DEPTNO と一致するものだけを抽出し、List を作り直す
        var filteredTestList = testList
            .Where(x => x.DEPTNO == request.DEPTNO)
            .ToList();

        // Assert
        Assert.Equal(realList.Count, filteredTestList.Count);

        // 以前作成した TEST_B1ResponseComparer を使って全プロパティを比較
        var comparer = TEST_B1ResponseComparer.Default;
        for (int i = 0; i < realList.Count; i++)
        {
            Assert.Equal(realList[i], filteredTestList[i], comparer);
        }
    }
#endif

}
