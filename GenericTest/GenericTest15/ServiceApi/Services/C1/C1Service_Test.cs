using ServiceApi.Requests.C1;
using ServiceApi.Responses.C1;

namespace ServiceApi.Services.C1;

public class C1Service_Test(string connectionString) : IC1Service
{
    private readonly string _ = connectionString; // connectionStringを無視

    public async IAsyncEnumerable<C1Response> ExecuteAsync(C1Request request)
    {
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

        // 大量データを想定してループで回す
        foreach (var res in responses)
        {
            await Task.Delay(1000); // 1件ごとに少し待機
            yield return res;
        }
    }
}
