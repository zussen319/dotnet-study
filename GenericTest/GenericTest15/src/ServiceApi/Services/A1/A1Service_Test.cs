using ServiceApi.Requests.A1;
using ServiceApi.Responses.A1;
using System.Runtime.CompilerServices;

namespace ServiceApi.Services.A1;

/*
 * API「A1」のサービスクラス（テスト用）
 */
public class A1Service_Test(string dummy)
    : TestServiceBase<A1Request, A1Response>(dummy)
{
    public override async IAsyncEnumerable<A1Response> ExecuteAsync(
        IEnumerable<A1Request> requests,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 検索開始前の初期遅延（クエリ実行待ちをシミュレート）
        await Task.Delay(2000, ct);

        // 大量データを想定してループで回す
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(1000, ct); // 1件ごとに少し待機
            yield return new A1Response { Id = i + 1, DataName = $"<A1Service_Test> Test Data {i + 1}" };
        }
    }
}
