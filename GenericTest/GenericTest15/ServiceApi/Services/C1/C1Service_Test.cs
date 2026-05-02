using ServiceApi.Requests.C1;
using ServiceApi.Responses.C1;

namespace ServiceApi.Services.C1;

public class C1Service_Test(string connectionString) : IC1Service
{
    private readonly string _ = connectionString; // connectionStringを無視

    public async IAsyncEnumerable<C1Response> ExecuteAsync(C1Request request)
    {
#if true
        yield return null;
#else
        // レスポンスデータを準備
        C1Response[] responses = [
            new() {
                EMPNO = 7788, ENAME = "SCOTT", JOB = "ANALYST", MGR = 7566,
                HIREDATE = "1987/04/19",SAL = 3000, COMM = null, DEPTNO = 20
            },
            new() {
                EMPNO = 7902, ENAME = "FORD", JOB = "ANALYST", MGR = 7566,
                HIREDATE = "1981/12/03",SAL = 3000, COMM = null, DEPTNO = 20
            },
            new() {
                EMPNO = 7566, ENAME = "JONES", JOB = "MANAGER", MGR = 7839,
                HIREDATE = "1981/04/02",SAL = 2975, COMM = null, DEPTNO = 20
            },
            new() {
                EMPNO = 7839, ENAME = "KING", JOB = "PRESIDENT", MGR = null,
                HIREDATE = "1981/11/17",SAL = 5000, COMM = null, DEPTNO = 10
            }
        ];

        // 検索開始前の初期遅延（クエリ実行待ちをシミュレート）
        await Task.Delay(2000);

        // 大量データを想定してループで回す
        foreach (var res in responses)
        {
            await Task.Delay(1000); // 1件ごとに少し待機
            yield return res;
        }
#endif
    }
}
