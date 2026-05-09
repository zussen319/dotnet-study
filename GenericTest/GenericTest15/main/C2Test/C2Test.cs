using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceApi;
using ServiceApi.Requests.C2;
using ServiceApi.Responses.C2;
using ServiceApi.Services.C2;

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
    .AddJsonFile("C2Test.json", optional: false, reloadOnChange: true)
    .Build();

#if true
bool testMode = args.Contains("-t");
string connStr = testMode
    ? "Data Source=localhost:1521/XE;Persist Security Inf=True;User ID=scott;Password=tiger"
    : config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;
var executor = new ApiExecutor();
#else
// アプリケーション構成の組み立て (Application Builder)
// 以下のパッケージが必要
// - Microsoft.Extensions.Hosting
var appBuilder = Host.CreateApplicationBuilder(args);
// 共通Executorを登録
appBuilder.Services.AddTransient<IApiExecutor, ApiExecutor>();

// メイン側で「テスト用か、本番用か」を判断して登録
bool testMode = args.Contains("-t");

if (testMode)
{
    // スタブではDB接続文字列は使用しないため任意の値でよい
    string connStr =
        "Data Source=localhost:1521/XE;Persisite Security Info=True;User ID=scott;Password=tiger";

    // テスト用を登録
    appBuilder.Services.AddTransient<IC2Service, C2Service_Test>(sp => new C2Service_Test(connStr));
}
else
{
    // DB接続文字列はメイン側で取得する
    string connStr = config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;

    // 本物を登録
    appBuilder.Services.AddTransient<IC2Service, C2Service>(sp => new C2Service(connStr));
}

// 構成を確定し実行ホストを生成
using IHost appHost = appBuilder.Build();

// -- 実行フェーズ --
var executor = appHost.Services.GetRequiredService<IApiExecutor>();
#endif
CancellationToken ct = default;

try
{
    string outputPath = @"C:\temp\C2Test.csv";
    var paramSection = config.GetSection("param");

    // （処理実行）検索結果を受け取る
#if true
    IEnumerable<C2Request> requests = 
        new[] { new C2Request { DEPTNO = paramSection.GetValue<decimal>("DEPTNO") } };
    var responseStream = testMode
        ? executor.RunAsync<C2Service_Test, C2Request, C2Response>(connStr, requests, ct)
        : executor.RunAsync<C2Service, C2Request, C2Response>(connStr, requests, ct);
#else
    // C2Service（本物）か C2Service_Test（ダミー）かはDIが自動判断
    var responseStream = executor.RunAsync<IC2Service, C2Request, C2Response>(
        [new C2Request { DEPTNO = paramSection.GetValue<decimal>("DEPTNO") }], ct);
#endif

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
            foreach (var member in response.Members)
            {
                string line2 = $"  {member.MEMBER_EMPNO},{member.MEMBER_ENAME}";
                await writer.WriteLineAsync(line2)/*.ConfigureAwait(false)*/;
                Console.WriteLine(line2);
                foreach (var staff in member.Staffs)
                {
                    string line3 = $"    {staff.STAFF_EMPNO},{staff.STAFF_ENAME}";
                    await writer.WriteLineAsync(line3)/*.ConfigureAwait(false)*/;
                    Console.WriteLine(line3);
                }
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
