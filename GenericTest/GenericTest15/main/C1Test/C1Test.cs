using Microsoft.Extensions.Configuration;
using ServiceApi;
using ServiceApi.Requests.C1;
using ServiceApi.Responses.C1;
using ServiceApi.Services.C1;

// プロジェクトのプロパティ
// ・ターゲットプラットフォーム： .NET 10.0
// ・ターゲットOS： Windows
// プロジェクト依存関係に「ServiceApi」を指定する

// 設定ファイルのビルド
// ConfigurationBuilderを使ってJSONを読み込む
// 以下のパッケージが必要
// - Microsoft.Extensions.Configuration.Binder
// - Microsoft.Extensions.Configuration.Json
// またJsonファイルのプロパティ「出力ディレクトリにコピー」は
// 「常にコピーする」「新しい場合はコピーする」を指定する
IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("C1Test.json", optional: false, reloadOnChange: true)
    .Build();

bool testMode = args.Contains("-t");
string connStr = testMode
    ? "Data Source=localhost:1521/XE;Persist Security Inf=True;User ID=scott;Password=tiger"
    : config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;
var executor = new ApiExecutor();
CancellationToken ct = default;

try
{
    string outputPath = @"C:\temp\C1Test.csv";
    var paramSection = config.GetSection("param");

    // （処理実行）検索結果を受け取る
    IEnumerable<C1Request> requests =
        new[] { new C1Request { DEPTNO = paramSection.GetValue<decimal>("DEPTNO") } };
    var responseStream = testMode
        ? executor.RunAsync<C1Service_Test, C1Request, C1Response>(connStr, requests, ct)
        : executor.RunAsync<C1Service, C1Request, C1Response>(connStr, requests, ct);

    // （結果取得）検索結果をファイルに書き出す
    using (var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8))
    {
        int count = 0;
        await foreach (var response in responseStream.WithCancellation(ct)/*.ConfigureAwait(false)*/)
        {
            // 取得データをCSV形式で書き出し
            string line1 = string.Join(",", new object?[]
            {
                response.DEPTNO,
                response.DNAME
            });
            await writer.WriteLineAsync(line1)/*.ConfigureAwait(false)*/;
            Console.WriteLine(line1);
            foreach (var emp in response.Employees)
            {
                string line2 = $"  {emp.EMPNO},{emp.ENAME}";
                await writer.WriteLineAsync(line2)/*.ConfigureAwait(false)*/;
                Console.WriteLine(line2);
            }

            count++;
            // コンソールには進捗を表示（大量データの場合は一定数ごとに出すと効率的です）
            //if (count % 100 == 0) Console.WriteLine($"{count} 件処理中...");
        }

        // 最後にバッファを強制的にフラッシュ（usingを抜ける際にも行われますが念のため）
        await writer.FlushAsync();
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation cancelled by user.");
}
catch (Exception ex)
{
    Console.WriteLine($"[Fatal Error] {ex.Message}");
}
