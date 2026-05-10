using ServiceApi.Requests.C2;
using ServiceApi.Responses.C2;

namespace ServiceApi.Services.C2;

public class C2Service_Test(string dummy)
    : TestServiceBase<C2Request, C2Response>(dummy)
{
    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
