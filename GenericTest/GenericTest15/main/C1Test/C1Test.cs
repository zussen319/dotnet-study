using Microsoft.Extensions.Configuration;
using ServiceApi;
using ServiceApi.Requests.C1;
using ServiceApi.Responses.C1;
using ServiceApi.Services.C1;

// プロジェクトのプロパティ
// ・ターゲットプラットフォーム： .NET 10.0
// ・ターゲットOS： Windows
// プロジェクト依存関係に「ServiceApi」を指定する
try
{
    // 実行モード（テスト用・本番用）
    bool testMode = args.Contains("-t");

    // ファイル出力先（確認用）
    string outputPath = @"C:\temp\C1Test.csv";

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

    // （API処理実行）検索結果を受け取る
    var executor = new ApiExecutor();
    CancellationToken ct = default;
    IAsyncEnumerable<C1Response> responseStream;
    if (testMode)
    {
        // テスト用（DB接続文字列・リクエストオブジェクトは参照されない）
        responseStream = executor.RunAsync<C1Service_Test, C1Request, C1Response>(
            string.Empty, Enumerable.Empty<C1Request>(), ct);
    } else
    {
        // 本番用
        string connStr = config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;
        IEnumerable<C1Request> requests = config.GetSection("param").Get<List<C1Request>>() ?? [];
        responseStream = executor.RunAsync<C1Service, C1Request, C1Response>(connStr, requests, ct);
    }

    // （結果取得）検索結果をファイルに書き出す
    using (var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8))
    {
        int count = 0;
        await foreach (var response in responseStream.WithCancellation(ct))
        {
            // 取得データをCSV形式で書き出し
            string line1 = string.Join(",", new object?[]
            {
                response.DEPTNO,
                response.DNAME
            });
            await writer.WriteLineAsync(line1);
            Console.WriteLine(line1);
            foreach (var emp in response.Employees)
            {
                string line2 = $"  {emp.EMPNO},{emp.ENAME}";
                await writer.WriteLineAsync(line2);
                Console.WriteLine(line2);
            }

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
