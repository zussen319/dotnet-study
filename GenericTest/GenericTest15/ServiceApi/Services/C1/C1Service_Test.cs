using ServiceApi.Requests.C1;
using ServiceApi.Responses.C1;

namespace ServiceApi.Services.C1;

public class C1Service_Test(string connectionString)
    : TestServiceBase<C1Request, C1Response>, IC1Service
{
    private readonly string _ = connectionString; // connectionStringを無視

    public async IAsyncEnumerable<C1Response> ExecuteAsync(C1Request request)
    {
#if true
        // レスポンスデータ準備（Jsonファイルから読み込み）
        // ファイル名は"<クラス名>.json"とし、カレントフォルダに配置する
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{this.GetType().Name}.json");
        List<C1Response> responses = await LoadJsonDataAsync(filePath);
#else
        // レスポンスデータを準備
        C1Response[] responses = [
            new() {
                DEPTNO = 910, DNAME = "<TEST>ACCOUNTING",
                Employees = [
                    new() { EMPNO = 9782, ENAME = "<TEST>CLARK" },
                    new() { EMPNO = 9839, ENAME = "<TEST>KING" },
                    new() { EMPNO = 9934, ENAME = "<TEST>MILLER" }
                ]
            },
            new() {
                DEPTNO = 920, DNAME = "<TEST>RESEARCH",
                Employees = [
                    new() { EMPNO = 9369, ENAME = "<TEST>SMITH" },
                    new() { EMPNO = 9566, ENAME = "<TEST>JONES" },
                    new() { EMPNO = 9788, ENAME = "<TEST>SCOTT" },
                    new() { EMPNO = 9876, ENAME = "<TEST>ADAMS" },
                    new() { EMPNO = 9902, ENAME = "<TEST>FORD" }
                ]
            }
        ];
#endif
        // 検索開始前の初期遅延（クエリ実行待ちをシミュレート）
        await Task.Delay(2000);

        // 大量データを想定してループで回す
        foreach (var res in responses)
        {
            await Task.Delay(1000); // 1件ごとに少し待機
            yield return res;
        }
    }
}
