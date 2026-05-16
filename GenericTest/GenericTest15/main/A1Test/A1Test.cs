using Microsoft.Extensions.Configuration;
using ServiceApi;
using ServiceApi.Requests.A1;
using ServiceApi.Responses.A1;
using ServiceApi.Services.A1;

/*
 * API「A1」のテスト用メインプログラム
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
        .AddJsonFile("A1Test.json", optional: false, reloadOnChange: true)
        .Build();

    /*
     * （API処理実行）検索結果を受け取る
     */
    ApiExecutor executor = new();
    // リクエストデータをconfigから取得
    IEnumerable<A1Request> requests =
        new[] { new A1Request { A1Value = config.GetSection("param").GetValue<decimal>("A1Value") }};
    IAsyncEnumerable<A1Response> responseStream;
    if (testMode)
    {
		// テスト用（DB接続文字列は参照されない）
		string connStr = "dummy";
		responseStream = executor.RunAsync<A1Service_Test, A1Request, A1Response>(connStr, requests);
    } else
    {
        // 本番用
        // DB接続文字列をconfigから取得
        string connStr = config.GetSection("config:ConnectionString").Get<string>() ?? string.Empty;
        responseStream = executor.RunAsync<A1Service, A1Request, A1Response>(connStr, requests);
    }

    /*
     * （結果取得）検索結果をファイルに書き出す
     */
    // 出力先ファイルパスをconfigから取得
    string outputPath = config.GetSection("config:OutputPath").Get<string>() ?? string.Empty;
    using (StreamWriter writer = new(outputPath, append: false, System.Text.Encoding.UTF8))
    {
        int count = 0;
        await foreach (A1Response response in responseStream)
        {
            // 取得データを書き出し
            string line = response.ToString();
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
catch (Exception ex)
{
    Console.WriteLine($"[Fatal Error] {ex.Message}");
}
