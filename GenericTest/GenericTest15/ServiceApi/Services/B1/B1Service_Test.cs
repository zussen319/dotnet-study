using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;
using ServiceApi.Responses.C1;

namespace ServiceApi.Services.B1;

public class B1Service_Test(string connectionString)
    : TestServiceBase<B1Request, B1Response>, IB1Service
{
    private readonly string _ = connectionString; // connectionStringを無視

    public async IAsyncEnumerable<B1Response> ExecuteAsync(B1Request request)
    {
#if true
        // レスポンスデータ準備（Jsonファイルから読み込み）
        // ファイル名は"<クラス名>.json"とし、カレントフォルダに配置する
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{this.GetType().Name}.json");
        List<B1Response> responses = await LoadJsonDataAsync(filePath);
#else
        // レスポンスデータを準備
        B1Response[] responses = [
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
