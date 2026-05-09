using ServiceApi.Requests.C2;
using ServiceApi.Responses.C2;

namespace ServiceApi.Services.C2;

/// <summary>
/// テスト用サービスクラス（C2）
/// </summary>
/// <param name="dummy">dummy</param>
public class C2Service_Test(string dummy)
    : TestServiceBase<C2Request, C2Response>(dummy)
{
    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
