using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;

namespace ServiceApi.Services.B1;

public class B1Service_Test(string connectionString) : IB1Service
{
    private readonly string _ = connectionString; // connectionStringを無視

    public async IAsyncEnumerable<B1Response> ExecuteAsync(B1Request request)
    {
        // 検索開始前の初期遅延（クエリ実行待ちをシミュレート）
        await Task.Delay(1000);

        // 大量データを想定してループで回す
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(2000); // 1件ごとに少し待機
            yield return new B1Response { EMPNO = i + 1000, ENAME = $"<B1Service_Test> Test Data {i + 1}" };
        }
    }
}
