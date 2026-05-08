using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceApi;
using ServiceApi.Requests.A1;
using ServiceApi.Responses.A1;
using ServiceApi.Services.A1;

// プロジェクトのプロパティ
// ・ターゲットプラットフォーム： .NET 10.0
// ・ターゲットOS： Windows
// プロジェクト依存関係に「ServiceApi」を指定する

// -- 登録フェーズ --

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

// ビルダを生成
// 以下のパッケージが必要
// - Microsoft.Extensions.Hosting
var builder = Host.CreateApplicationBuilder(args);
// 共通Executorを登録
builder.Services.AddTransient<IApiExecutor, ApiExecutor>();

// メイン側で「テスト用か、本番用か」を判断して登録
bool testMode = args.Contains("-t");

if (testMode)
{
    // スタブではDB接続文字列は使用しないため任意の値でよい
    string connStr =
        "Data Source=localhost:1521/XE;Persisite Security Info=True;User ID=scott;Password=tiger";

    // テスト用を登録
    builder.Services.AddTransient<IA1Service, A1Service_Test>(sp => new A1Service_Test(connStr));
}
else
{
    // DB接続文字列はメイン側で取得する
    string connStr = config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;

    // 本物を登録
    builder.Services.AddTransient<IA1Service, A1Service>(sp => new A1Service(connStr));
}

using IHost host = builder.Build();

// -- 実行フェーズ --
var executor = host.Services.GetRequiredService<IApiExecutor>();

try
{
    string outputPath = @"C:\temp\A1Test.csv";
    var paramSection = config.GetSection("param");

    // （処理実行）検索結果を受け取る
    // A1Service（本物）か A1Service_Test（ダミー）かはDIが自動判断
    var responseStream = executor.RunAsync<IA1Service, A1Request, A1Response>(
        [new A1Request
        {
            A1Value = paramSection.GetValue<decimal>("A1Value")
        }]);

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
