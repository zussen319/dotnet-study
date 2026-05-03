using ServiceApi.Requests.C2;
using ServiceApi.Responses.C2;

namespace ServiceApi.Services.C2;

public class C2Service_Test(string connectionString)
    : TestServiceBase<C2Request, C2Response>, IC2Service
{
    private readonly string _ = connectionString; // connectionStringを無視

    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
