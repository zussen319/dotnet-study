using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;

namespace ServiceApi.Services.B1;

/// <summary>
/// テスト用サービスクラス（B1）
/// </summary>
/// <param name="dummy">dummy</param>
public class B1Service_Test(string dummy)
    : TestServiceBase<B1Request, B1Response>(dummy)
{
    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
