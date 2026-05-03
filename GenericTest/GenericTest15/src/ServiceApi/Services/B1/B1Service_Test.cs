using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;

namespace ServiceApi.Services.B1;

public class B1Service_Test(string connectionString)
    : TestServiceBase<B1Request, B1Response>, IB1Service
{
    private readonly string _ = connectionString; // connectionStringを無視

    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
