using Microsoft.Extensions.Logging;

namespace ApiProject;

public class ApiService {
	// ロガー自体はクラスの不変のメンバとして定義（Serilog等と同じ構成）
	private readonly ILogger _logger = new CustomLogger("ApiService");

	public List<object> Execute(int flag)
	{
		// 処理の開始時に「スコープ」を開始する。
		// この using は、将来 Serilog に変えても「そのまま全く同じコード」で動作します。
		using var scope = _logger.BeginScope("ExecuteContext");

		try {
			_logger.LogInformation("API> Data processing started.");

			if (flag % 2 == 0) throw new Exception("exception simulation");

			_logger.LogInformation("API> Data processing success.");
			return [];
		} catch (Exception ex) {
			// カスタムロガーから直接相関IDを取得したい場合はキャストして取得
			string currentId = (_logger as CustomLogger)?.CorrelationId ?? "N/A";

			_logger.LogError(ex, "API> Exception occurred during operation.");
			throw new InvalidOperationException($"Caught API side exception [API-ID: {currentId}]", ex);
		}
		// using を抜けた瞬間、自動的にこの処理専用のログファイルが閉じられます
	}
}
