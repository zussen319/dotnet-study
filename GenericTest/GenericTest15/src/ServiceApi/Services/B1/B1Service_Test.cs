using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;

namespace ServiceApi.Services.B1;

/*
 * API「B1」のサービスクラス（テスト用）
 */
public class B1Service_Test(string dummyStr, int dummyRows = 0)
    : TestServiceBase<B1Request, B1Response>(dummyStr, dummyRows)
{
    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
