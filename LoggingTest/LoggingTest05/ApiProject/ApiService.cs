using Microsoft.Extensions.Logging;

namespace ApiProject;

public class ApiService
{
	// ロガー自体はクラスの不変のメンバとして定義
	// （相関IDは内部で自動的に生成される）
	private readonly ILogger _logger = new CustomLogger("ApiService");

	public List<object> Execute(int flag)
	{
		// 処理の開始時にスコープを開始する
		// （このusingは将来Serilogに変えてもそのまま全く同じコードで動作する）
		using var scope = _logger.BeginScope("ExecuteContext");

		try {
			_logger.LogInformation("API> Data processing started.");

			if (flag % 2 == 0) throw new Exception("simulate exception");

			_logger.LogInformation("API> Data processing success.");
			return [];
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "API> Exception occurred during operation.");

			// 相関IDをメインプログラム側に通知
			// カスタムロガーから直接相関IDを取得したい場合はキャストして取得
			string currentId = (_logger as CustomLogger)?.CorrelationId ?? "N/A";
			throw new InvalidOperationException($"Caught API side exception [API-ID: {currentId}]", ex);
		}
		// usingを抜けた時に自動的にログファイルがクローズされる
	}
}
