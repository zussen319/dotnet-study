using ServiceApi.Requests.C1;
using ServiceApi.Responses.C1;

namespace ServiceApi.Services.C1;

/*
 * API「C1」のサービスクラス（テスト用）
 */
public class C1Service_Test(string dummyStr, int dummyRows = 0)
    : TestServiceBase<C1Request, C1Response>(dummyStr, dummyRows)
{
    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
