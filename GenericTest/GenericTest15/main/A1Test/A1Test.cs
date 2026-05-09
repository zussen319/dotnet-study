using Microsoft.Extensions.Configuration;
using ServiceApi;
using ServiceApi.Requests.A1;
using ServiceApi.Responses.A1;
using ServiceApi.Services.A1;

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
    .AddJsonFile("A1Test.json", optional: false, reloadOnChange: true)
    .Build();

bool testMode = args.Contains("-t");
string connStr = testMode
    ? "Data Source=localhost:1521/XE;Persist Security Inf=True;User ID=scott;Password=tiger"
    : config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;
var executor = new ApiExecutor();

try
{
    string outputPath = @"C:\temp\A1Test.csv";
    var paramSection = config.GetSection("param");

    // （処理実行）検索結果を受け取る
    IEnumerable<A1Request> requests = 
        new[] { new A1Request { A1Value = paramSection.GetValue<decimal>("A1Value") } };
    var responseStream = testMode
        ? executor.RunAsync<A1Service_Test, A1Request, A1Response>(connStr, requests)
        : executor.RunAsync<A1Service, A1Request, A1Response>(connStr, requests);

    // （結果取得）検索結果をファイルに書き出す
    using (var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8))
    {
        int count = 0;
        await foreach (var response in responseStream)
        {
            // DBから届いたデータを即座にCSV形式で書き出し
            string line = string.Join(",", new object?[] { response.Id, response.DataName });
            await writer.WriteLineAsync(line);
            Console.WriteLine(line);

            count++;
            // コンソールには進捗を表示（大量データの場合は一定数ごとに出すと効率的です）
            //if (count % 100 == 0) Console.WriteLine($"{count} 件処理中...");
        }

        // 最後にバッファを強制的にフラッシュ（usingを抜ける際にも行われますが念のため）
        await writer.FlushAsync();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[Fatal Error] {ex.Message}");
}
