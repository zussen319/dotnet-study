using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ApiProject;

public class CustomLogger : ILogger
{
	private readonly string _categoryName;
	private readonly LogLevel _minimumLogLevel = LogLevel.Information;
	private readonly string _directoryPath = "logs";
	private readonly string _outputTemplate = "{Timestamp} [{Level}] - {Message}";

	// スレッド（処理）ごとに現在開いているストリームと相関IDを保持する
	private readonly AsyncLocal<LogContext?> _currentContext = new();

	public string CorrelationId => _currentContext.Value?.CorrelationId ?? "N/A";

	public CustomLogger(string categoryName)
	{
		_categoryName = categoryName;
		string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logsettings.json");

		if (File.Exists(configPath)) {
			try {
				using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
				if (doc.RootElement.TryGetProperty("LogSettings", out var logSettings) &&
					logSettings.TryGetProperty(categoryName, out var config))
				{
					if (config.TryGetProperty("MinimumLevel", out var lvl)) _minimumLogLevel = ParseLogLevel(lvl.GetString());
					if (config.TryGetProperty("DirectoryPath", out var dir)) _directoryPath = dir.GetString() ?? _directoryPath;
					if (config.TryGetProperty("OutputTemplate", out var tmp)) _outputTemplate = tmp.GetString() ?? _outputTemplate;
				}
			} catch { /* デフォルト値動作 */ }
		}
	}

	// ★ ここが最大のポイント：BeginScope でファイルを開き、閉じるための枠組みを返す
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
	{
		string correlationId = Guid.NewGuid().ToString("N")[..8];

		if (!Directory.Exists(_directoryPath)) {
			Directory.CreateDirectory(_directoryPath);
		}

		string timestampStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string fullFilePath = Path.Combine(_directoryPath, $"{_categoryName}_{timestampStr}_{correlationId}.log");

		var writer = new StreamWriter(fullFilePath, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };

		// コンテキストの生成
		_currentContext.Value = new LogContext(writer, correlationId);

		// 使い終わったら破棄（ファイルをクローズ）するトリガー（IDisposable）を返す
		return _currentContext.Value;
	}

	public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLogLevel;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel)) return;

		// 現在のコンテキスト（ファイル）がなければ書き込まない
		var context = _currentContext.Value;
		if (context == null) return;

		string message = formatter(state, exception);
		if (exception != null) message += $"{Environment.NewLine}{exception}";

		string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		string threadId = Environment.CurrentManagedThreadId.ToString();
		string levelLabel = logLevel switch {
			LogLevel.Debug => "DEBUG",
			LogLevel.Information => "INFO ",
			LogLevel.Warning => "WARN ",
			LogLevel.Error => "ERROR",
			_ => "INFO "
		};

		string formattedLine = _outputTemplate
			.Replace("{Timestamp}", timestamp)
			.Replace("{ThreadId}", threadId)
			.Replace("{Level}", levelLabel)
			.Replace("{CorrelationId}", context.CorrelationId)
			.Replace("{Message}", message);

		context.Writer.WriteLine(formattedLine);
	}

	private static LogLevel ParseLogLevel(string? levelStr) => levelStr?.ToUpper() switch {
		"DEBUG" => LogLevel.Debug,
		"INFO" => LogLevel.Information,
		"WARN" => LogLevel.Warning,
		"ERROR" => LogLevel.Error,
		_ => LogLevel.Information
	};

	// ログの開閉状態とIDを管理するインナークラス
	private class LogContext : IDisposable {
		public StreamWriter Writer { get; }
		public string CorrelationId { get; }

		public LogContext(StreamWriter writer, string correlationId)
		{
			Writer = writer;
			CorrelationId = correlationId;
		}

		public void Dispose()
		{
			Writer.Dispose();
		}
	}
}
