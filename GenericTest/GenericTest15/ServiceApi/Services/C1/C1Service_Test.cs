using ServiceApi.Requests.C1;
using ServiceApi.Responses.C1;

namespace ServiceApi.Services.C1;

public class C1Service_Test(string connectionString)
    : TestServiceBase<C1Request, C1Response>, IC1Service
{
    private readonly string _ = connectionString; // connectionStringを無視

    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
