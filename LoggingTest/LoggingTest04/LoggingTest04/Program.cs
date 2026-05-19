using ApiProject;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

using ILogger = Microsoft.Extensions.Logging.ILogger;


/*
 * メインプログラム側に最低限してもらう3つのこと
 * 
 * 1. appsettings.json に「ApiSerilog」の定義を残すこと
 * メインプログラム側の開発担当者が「もうログ出力をやめるから」といって
 * appsettings.json 自体を削除したり中身を空にしてしまうと
 * API側が設定を読み込めなくなります。
 * メイン側の設定（MainSerilog）は丸ごと消去して構いませんが
 * ApiSerilog のセクションだけは必ずファイル内に残してもらう必要があります。
 {
  // MainSerilog は消してしまってOK
  "ApiSerilog": {
    "Using": [ "Serilog.Sinks.File", "Serilog.Enrichers.Thread" ],
    "MinimumLevel": "Information",
    "Enrich": [ "WithThreadId" ],
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "logs/api_service.log",
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{ThreadId}] {Level:u3} [{CorrelationId}] - {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  }
}
 *
 * 2.appsettings.json の「出力ディレクトリにコピー」設定を維持すること
 * メイン側で appsettings.json を管理している場合、そのファイルのプロパティ設定である
 * 「出力ディレクトリにコピー：新しい場合はコピー（または常にコピー）」 の設定を変えないように
 * 申し送りしてください。ここが「コピーしない」に戻されると
 * 実行フォルダにファイルが配置されず、API側が設定を見失います。
 *
 * 3. メイン側の .csproj から NuGet パッケージを削除しないこと
 * メインプログラム側のコードからSerilogの記述が一切なくなったとしても
 * メインプログラム側の .csproj に記述した以下のパッケージ参照
 * （および <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> の設定）
 * は削除せず残してもらう必要があります。
 *  * Serilog.Sinks.File
 *  * Serilog.Enrichers.Thread
 * exeとしてビルドされて実行されるのはメインプログラム側であるため
 * メイン側のビルドシステムが「これらのDLLを実行フォルダに出力する」という指示
 * （パッケージ参照）を保持し続けてくれないと、API側が必要とするDLLが実行フォルダから消えてしまい
 * 「ファイルが見つからない」という実行時エラーに戻ってしまいます。
 */

// メイン用の設定読み込みとSerilog設定
var configuration = new ConfigurationBuilder()
	.SetBasePath(Directory.GetCurrentDirectory())
	.AddJsonFile("logsettings.json", optional: false, reloadOnChange: true)
	.Build();

var mainReaderOptions = new Serilog.Settings.Configuration.ConfigurationReaderOptions {
	SectionName = "MainSerilog"
};

Log.Logger = new LoggerConfiguration()
	.ReadFrom.Configuration(configuration, mainReaderOptions)
	// .Enrich.With... の行は削除
	.CreateLogger();

using var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory();
ILogger mainLogger = loggerFactory.CreateLogger("MainProject");

var apiService = new ApiService();

// --- 業務ロジック ---
mainLogger.LogInformation("Main> Application start.");
try {
	for (int flag = 1; flag <= 2; flag++) {
		mainLogger.LogInformation("Main> Invoking API. [flag: {flag}]", flag);
		var result = apiService.Execute(flag);
		mainLogger.LogInformation("Main> API completed. [flag: {flag}]", flag);
	}
} catch (Exception ex) {
	mainLogger.LogError(ex, "Main> API failed with critical error.");
}
mainLogger.LogInformation("Main> Application end.");

Log.CloseAndFlush();
