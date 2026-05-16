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
    // DB接続文字列：テスト用設定ファイル（ServiceApi.Test.Json）から取得
    private readonly string _connectionString =
        TEST_ConfigurationManager.GetValue<string>(ConfigId.ConnectionString);

    [Theory]
    [InlineData(20,true)]   // 存在するデータ (dataExists:true)
    [InlineData(999,false)] // 存在しないデータ (dataExists:false)
    public async Task ExecuteAsync_正常系_検索処理_01(decimal deptNo, bool dataExists)
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
        /*
         * ストリーム使用
         */
        Func<DbDataReader, B1Response> mapFunc = r => new B1Response
        {
            EMPNO = Convert.ToDecimal(r["EMPNO"]), // NOT NULL
            ENAME = Convert.ToString(r["ENAME"]) ?? string.Empty,
            JOB = Convert.ToString(r["JOB"]) ?? string.Empty,
            MGR = r["MGR"] is DBNull ? null : Convert.ToDecimal(r["MGR"]),
            HIREDATE = Convert.ToString(r["HIREDATE"]) ?? string.Empty,
            SAL = r["SAL"] is DBNull ? null : Convert.ToDecimal(r["SAL"]),
            COMM = r["COMM"] is DBNull ? null : Convert.ToDecimal(r["COMM"]),
            DEPTNO = r["DEPTNO"] is DBNull ? null : Convert.ToDecimal(r["DEPTNO"])
        };
        List<B1Response> expectList = [];
        await foreach (var item in dbm.ExecuteQueryAsync<B1Response>(sql, bindAction, mapFunc))
        {
            expectList.Add(item);
        }
#else
        /*
         * DataTable使用
         */
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

        // テストデータ準備確認（deptNo指定）
        // ・存在するはずのデータが存在しない
        // ・存在しないはずのデータが存在する
        Assert.True((dataExists == (expectList.Count > 0)), $"テストデータ指定誤り (deptNo:{deptNo})");

        //
        // テスト実行
        //
        B1Service service = new(_connectionString);
        IEnumerable<B1Request> requests = [new() { DEPTNO = deptNo }];
        List<B1Response> resultList = [];
        await foreach (B1Response item in service.ExecuteAsync(requests))
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
            //isMatch = expectList.Contains(result, TEST_B1ResponseComparer.Default);
            isMatch = expectList.Contains(result);
            if (isMatch == false) { break; }
        }
        Assert.True(isMatch);
    }

    [Fact]
    public async Task ExecuteAsync_異常系_実行キャンセル確認_01()
    {
        /*
         * キャンセルのテストを組み込む場合、「タイムアウトや外部からのキャンセルによって、
         * 意図したタイミングで例外が投げられるか」を検証するメソッドを追加するのが一般的です。
         * xUnitでは、非同期の例外検証に Assert.ThrowsAsync<OperationCanceledException> を使用します。
         * 
         * 実DB接続を伴うため、SQL実行中やフェッチ中にキャンセルが発生することをシミュレートします。
         */
        // 1. 準備 (Arrange)
        B1Service service = new(_connectionString);
        IEnumerable<B1Request> requests = [new() { DEPTNO = 20 }];

        // 即座にキャンセルされるトークンを作成
        using CancellationTokenSource cts = new();
        cts.Cancel(); // 実行前にキャンセル状態にする

        // 2. 実行 & 3. 検証 (Act & Assert)
        // ExecuteAsync 自体は IAsyncEnumerable を返すだけなので、
        // 実際に列挙を始めた（MoveNextAsyncが呼ばれた）タイミングで例外が発生することを検証
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (B1Response item in service.ExecuteAsync(requests, cts.Token))
            {
                // ここには到達しないはず
            }
        });
    }

#if false
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
