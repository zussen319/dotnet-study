using ApiProject;
using Microsoft.Extensions.Logging;

// メインプログラム用のロガーを生成（インスタンスはアプリ内で使い回す）
// この時点ではまだログファイルは作成されません
var mainLogger = new CustomLogger("MainApp");
ILogger logger = mainLogger;

// 業務ロジックの開始に合わせてスコープを生成
// この using を抜けた瞬間に、自動的にメイン用のログファイルが閉じられます
using (var mainScope = logger.BeginScope("MainProcessContext")) {
	logger.LogInformation("Main> Application start.");
	logger.LogInformation($"Main> Current Main Log ID is: {mainLogger.CorrelationId}");

	var apiService = new ApiService();

	try {
		for (int flag = 1; flag <= 2; flag++) {
			logger.LogInformation("Main> Invoking API. [flag: {flag}]", flag);

			// APIの呼び出し（API内部でも独自のScopeが走り、個別のログファイルが生成されます）
			var result = apiService.Execute(flag);

			logger.LogInformation("Main> API completed. [flag: {flag}]", flag);
		}
	} catch (Exception ex) {
		logger.LogError(ex, "Main> API failed with critical error.");
	}

	logger.LogInformation("Main> Application end.");
}
// ★ ここで mainScope が破棄され、main.log のファイルハンドルが安全に解放されます。
// 明示的に logger.Close() を呼ぶ必要はありません。
