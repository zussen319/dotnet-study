using ServiceApi.Requests.A1;
using ServiceApi.Responses.A1;

namespace ServiceApi.Services.A1;

public class A1Service_Test(string connectionString)
    : TestServiceBase<A1Request, A1Response>, IA1Service
{
    private readonly string _ = connectionString; // connectionStringを無視

    public override async IAsyncEnumerable<A1Response> ExecuteAsync(A1Request request)
    {
        // 検索開始前の初期遅延（クエリ実行待ちをシミュレート）
        await Task.Delay(2000);

        // 大量データを想定してループで回す
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(1000); // 1件ごとに少し待機
            yield return new A1Response { Id = i + 1, DataName = $"<A1Service_Test> Test Data {i + 1}" };
        }
    }
}
