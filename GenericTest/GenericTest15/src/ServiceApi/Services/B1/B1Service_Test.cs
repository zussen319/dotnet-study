using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;

namespace ServiceApi.Services.B1;

/*
 * API「B1」のサービスクラス（テスト用）
 */
public class B1Service_Test(string dummy)
    : TestServiceBase<B1Request, B1Response>(dummy)
{
    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
