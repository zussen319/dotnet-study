using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceApi;
using ServiceApi.Requests.C1;
using ServiceApi.Responses.C1;
using ServiceApi.Services.C1;

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
    .AddJsonFile("C1Test.json", optional: false, reloadOnChange: true)
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
    builder.Services.AddTransient<IC1Service, C1Service_Test>(sp => new C1Service_Test(connStr));
}
else
{
    // DB接続文字列はメイン側で取得する
    string connStr = config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;

    // 本物を登録
    builder.Services.AddTransient<IC1Service, C1Service>(sp => new C1Service(connStr));
}

using IHost host = builder.Build();
CancellationToken ct = default;

// -- 実行フェーズ --
var executor = host.Services.GetRequiredService<IApiExecutor>();

try
{
    string outputPath = @"C:\temp\C1Test.csv";
    var paramSection = config.GetSection("param");

    // （処理実行）検索結果を受け取る
    // C1Service（本物）か C1Service_Test（ダミー）かはDIが自動判断
    var responseStream = executor.RunAsync<IC1Service, C1Request, C1Response>(new C1Request {}, ct);

    // （結果取得）検索結果をファイルに書き出す
    using (var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8))
    {
        int count = 0;
        await foreach (var response in responseStream.WithCancellation(ct).ConfigureAwait(false))
        {
            // 取得データをCSV形式で書き出し
            string line1 = string.Join(",", new object?[]
            {
                response.DEPTNO,
                response.DNAME
            });
            await writer.WriteLineAsync(line1).ConfigureAwait(false);
            Console.WriteLine(line1);
            foreach (var emp in response.Employees)
            {
                string line2 = $"  {emp.EMPNO},{emp.ENAME}";
                await writer.WriteLineAsync(line2).ConfigureAwait(false);
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
