using Microsoft.Extensions.Configuration;
using ServiceApi;
using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;
using ServiceApi.Services.B1;

// プロジェクトのプロパティ
// ・ターゲットプラットフォーム： .NET 10.0
// ・ターゲットOS： Windows
// プロジェクト依存関係に「ServiceApi」を指定する
try
{
    // 実行モード（テスト用・本番用）
    bool testMode = args.Contains("-t");

    // ファイル出力先（確認用）
    string outputPath = @"C:\temp\B1Test.csv";

    // 設定ファイルのビルド
    // ConfigurationBuilderを使ってJSONを読み込む
    // 以下のパッケージが必要
    // - Microsoft.Extensions.Configuration.Binder
    // - Microsoft.Extensions.Configuration.Json
    // またJsonファイルのプロパティ「出力ディレクトリにコピー」は
    // 「常にコピーする」「新しい場合はコピーする」を指定する
    IConfiguration config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("B1Test.json", optional: false, reloadOnChange: true)
        .Build();

    var paramSection = config.GetSection("param");

    // （API処理実行）検索結果を受け取る
    var executor = new ApiExecutor();
    IEnumerable<B1Request> requests =
        new[] { new B1Request { DEPTNO = paramSection.GetValue<decimal>("DEPTNO") } };
    CancellationToken ct = default;
    IAsyncEnumerable<B1Response> responseStream;
    if (testMode)
    {
        // テスト用（DB接続文字列は任意）
        string connStr =
            "Data Source=localhost:1521/XE;Persist Security Inf=True;User ID=scott;Password=tiger";
        responseStream = executor.RunAsync<B1Service_Test, B1Request, B1Response>(connStr, requests, ct);
    } else
    {
        // 本番用
        string connStr = config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;
        responseStream = executor.RunAsync<B1Service, B1Request, B1Response>(connStr, requests, ct);
    }

    // （結果取得）検索結果をファイルに書き出す
    using (var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8))
    {
        int count = 0;
        await foreach (var response in responseStream.WithCancellation(ct))
        {
            // 取得データをCSV形式で書き出し
            string line = string.Join(",", new object?[]
            {
                response.EMPNO,
                response.ENAME,
                response.JOB,
                response.MGR,
                response.HIREDATE,
                response.SAL,
                response.COMM,
                response.DEPTNO
            });
            await writer.WriteLineAsync(line);
            Console.WriteLine(line);

            count++;
            // コンソールに進捗を表示
            //if (count % 100 == 0) Console.WriteLine($"{count} 件処理中...");
        }

        // 最後にバッファを強制的にフラッシュ
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
