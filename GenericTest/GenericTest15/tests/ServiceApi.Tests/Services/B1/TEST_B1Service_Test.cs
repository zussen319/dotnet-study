using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;
using ServiceApi.Services.B1;
using ServiceApi.Tests.Common;

namespace ServiceApi.Tests.Services.B1;

public class TEST_B1Service_Test
{
    /*
     * Json形式でデータを取得するSQL：
        SET PAGESIZE 0
        SET FEEDBACK OFF
        SET LINESIZE 32767
        SET LONG 2000000
        SET TRIMSPOOL ON
        SPOOL 'C:\temp\B1Service_Test.txt'
        SELECT JSON_ARRAYAGG(
            JSON_OBJECT(
                'EMPNO'    VALUE EMPNO,
                'ENAME'    VALUE ENAME,
                'JOB'      VALUE JOB,
                'MGR'      VALUE MGR,
                'HIREDATE' VALUE TO_CHAR(HIREDATE, 'yyyy/mm/dd'),
                'SAL'      VALUE SAL,
                'COMM'     VALUE COMM,
                'DEPTNO'   VALUE DEPTNO
            ) RETURNING CLOB
        )
        FROM EMP ORDER BY EMPNO;
        SPOOL OFF
    */
    //[Fact(DisplayName = "B1Service_Test：JSONデータ読み込み")]
    [Fact]
    public async Task ExecuteAsync_JSONデータ読み込み()
    {
        /*
         * 指定したJsonファイルを読み込み、B1Responseオブジェクトを生成できること
         */
        // Jsonファイルを読み込み、List<B1Response>オブジェクトを生成する
        string fileName = "B1Service_Test.json";
        //var expectedList = TestDataLoader.LoadJsonData<B1Response>(fileName);
        var expectedList = TEST_JsonManipulator.LoadJsonData<B1Response>(fileName);

        // 1. 準備 (Arrange)
        // サービスをインスタンス化
        var service = new B1Service_Test("dummy_connection");
        var request = new B1Request { DEPTNO = 999 };

        // 2. 実行 (Act)
        var results = new List<B1Response>();
        await foreach (var item in service.ExecuteAsync(request))
        {
            // IAsyncEnumerableをListに変換して中身を検証しやすくする
            results.Add(item);
        }

        // 3. 検証 (Assert)
        // B1Service_Test.ExecuteAsyncがJsonファイルを正常に読み込み返却すること
        // ・例外が発生しないこと
        // ・テストコード内で読み込んだ結果と件数が一致すること
        Assert.Equal(expectedList.Count, results.Count);
    }

    [Fact]
    public async Task ExecuteAsync_待機中にキャンセルされ中断すること()
    {
        /*
         * スタブ側（TestServiceBase）は Task.Delay を含んでいるため
         * 「処理の途中でキャンセルされる」という、より現実的なテストが可能です。
         */
        // 1. 準備 (Arrange)
        var service = new B1Service_Test("dummy_connection");
        var request = new B1Request { DEPTNO = 999 };

        // 500ms後にキャンセルを発動させる
        // スタブには 2000ms (初期遅延) + 1000ms (1件ごと) の待機があるため、途中で止まるはず
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        // 2. 実行 & 3. 検証 (Act & Assert)
#if true
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in service.ExecuteAsync(request, cts.Token))
            {
                // 処理
            }
        });
#else
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in service.ExecuteAsync(request, cts.Token))
            {
                // 成功（ここに来る前に止まるはず）
            }
        });
#endif

        // トークンが正しく紐付いているかも確認可能
        Assert.Equal(cts.Token, exception.CancellationToken);
    }
}