using Microsoft.Extensions.Configuration;
using ServiceApi;
using ServiceApi.Requests.C2;
using ServiceApi.Responses.C2;
using ServiceApi.Services.C2;

/*
 * API「C2」のテスト用メインプログラム
 */
// プロジェクトのプロパティ
// ・ターゲットプラットフォーム： .NET 10.0
// ・ターゲットOS： Windows
// プロジェクト依存関係に「ServiceApi」を指定する
try
{
    // 実行モード（テスト用・本番用）
    // 起動時引数に"-t"が指定されたらテストモードで実行
    bool testMode = args.Contains("-t");

    // 設定ファイルのビルド
    // ConfigurationBuilderを使ってJsonファイルを読み込む
    // 以下のパッケージが必要
    // - Microsoft.Extensions.Configuration.Binder
    // - Microsoft.Extensions.Configuration.Json
    // またJsonファイルのプロパティ「出力ディレクトリにコピー」は
    // 「常にコピーする」「新しい場合はコピーする」を指定する
    IConfiguration config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("C2Test.json", optional: false, reloadOnChange: true)
        .Build();

    /*
     * （API処理実行）検索結果を受け取る
     */
    ApiExecutor executor = new();
    // リクエストデータをconfigから取得
    IEnumerable<C2Request> requests = config.GetSection("param").Get<List<C2Request>>() ?? [];
    CancellationToken ct = default;
    IAsyncEnumerable<C2Response> responseStream;
    if (testMode)
    {
        // テスト用（DB接続文字列は参照されない）
        responseStream = executor.RunAsync<C2Service_Test, C2Request, C2Response>(string.Empty, requests, ct);
    } else
    {
        // 本番用
        // DB接続文字列をconfigから取得
        string connStr = config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;
        responseStream = executor.RunAsync<C2Service, C2Request, C2Response>(connStr, requests, ct);
    }

    /*
     * （結果取得）検索結果をファイルに書き出す
     */
    // 出力先ファイルパスをconfigから取得
    string outputPath = config.GetSection("config:OutputPath").Get<string>() ?? string.Empty;
    using (StreamWriter writer = new(outputPath, append: false, System.Text.Encoding.UTF8))
    {
        int count = 0;
        await foreach (C2Response response in responseStream.WithCancellation(ct))
        {
            // 取得データを書き出し
            string line1 = string.Join(",", new object?[]
            {
                response.DEPTNO,
                response.DNAME
            });
            await writer.WriteLineAsync(line1);
            Console.WriteLine(line1);
            foreach (C2Response.Member member in response.Members)
            {
                string line2 = $"  {member.MEMBER_EMPNO},{member.MEMBER_ENAME}";
                await writer.WriteLineAsync(line2);
                Console.WriteLine(line2);
                foreach (C2Response.Staff staff in member.Staffs)
                {
                    string line3 = $"    {staff.STAFF_EMPNO},{staff.STAFF_ENAME}";
                    await writer.WriteLineAsync(line3);
                    Console.WriteLine(line3);
                }
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
