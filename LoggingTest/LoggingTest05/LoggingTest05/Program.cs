using ApiProject;
using Microsoft.Extensions.Logging;

// メインプログラム用のロガーを生成（インスタンスはアプリ内で使い回す）
// この時点ではまだログファイルは作成されない
ILogger logger = new CustomLogger("MainApp");

// 業務ロジックの開始に合わせてスコープを生成
// usingを抜けた時に自動的にメイン用のログファイルがクローズされる
using (var mainScope = logger.BeginScope("MainProcessContext"))
{
	logger.LogInformation("Main> Application start.");
	logger.LogInformation($"Main> Current Main Log ID is: {(logger as CustomLogger)?.CorrelationId}");

	var apiService = new ApiService();

	try {
		for (int flag = 1; flag <= 2; flag++) {
			logger.LogInformation("Main> Invoking API. [flag: {flag}]", flag);

			// APIの呼び出し
			var result = apiService.Execute(flag);

			logger.LogInformation("Main> API completed. [flag: {flag}]", flag);
		}
	} catch (Exception ex) {
		logger.LogError(ex, "Main> API failed with critical error.");
	}

	logger.LogInformation("Main> Application end.");
}
// ここでmainScopeが破棄され、main.logのファイルハンドルが解放される
