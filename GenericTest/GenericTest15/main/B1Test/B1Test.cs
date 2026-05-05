using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceApi;
using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;
using ServiceApi.Services.B1;

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
    .AddJsonFile("B1Test.json", optional: false, reloadOnChange: true)
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
    builder.Services.AddTransient<IB1Service, B1Service_Test>(sp => new B1Service_Test(connStr));
}
else
{
    // DB接続文字列はメイン側で取得する
    string connStr = config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;

    // 本物を登録
    builder.Services.AddTransient<IB1Service, B1Service>(sp => new B1Service(connStr));
}

using IHost host = builder.Build();

// -- 実行フェーズ --
var executor = host.Services.GetRequiredService<IApiExecutor>();

// --- キャンセル検証用テストコード ---
// CancellationTokenSource を作成
// 500ms 後に自動的にキャンセルを発動させる（スタブの Delay 2000ms より先に動く）
//using var cts = new CancellationTokenSource();
//cts.CancelAfter(TimeSpan.FromMilliseconds(500));

try
{
    string outputPath = @"C:\temp\B1Test.csv";
    var paramSection = config.GetSection("param");

    // （処理実行）非同期ストリームとして受け取る
    // B1Service（本物）か B1Service_Test（ダミー）かはDIが自動判断
    var responseStream = executor.RunAsync<IB1Service, B1Request, B1Response>(
        new B1Request { DEPTNO = paramSection.GetValue<int>("DEPTNO")} /*, cts.Token */);

    // （結果取得）非同期でファイルを書き出す
    using (var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8))
    {
        int count = 0;
        await foreach (var response in responseStream)
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
