using ServiceApi.Requests.C2;
using ServiceApi.Responses.C2;

namespace ServiceApi.Services.C2;

/*
 * API「C2」のサービスクラス（テスト用）
 */
public class C2Service_Test(string dummyStr, int dummyRows = 0)
    : TestServiceBase<C2Request, C2Response>(dummyStr, dummyRows)
{
    // テストサービスクラスのExecuteAsyncはベースクラスで実装
}
