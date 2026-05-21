using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ApiProject;

public class CustomLogger : ILogger
{
	// デフォルト値
	// （設定ファイルが読み込まれない、または設定ファイルに指定がない場合に使用する）

	// ログカテゴリ名
	private readonly string _categoryName;
	// ロギングレベル
	private readonly LogLevel _minimumLogLevel = LogLevel.Information;
	// ログ出力先フォルダパス
	private readonly string _directoryPath = "logs";
	// ログ出力フォーマット
	private readonly string _outputTemplate = "{Timestamp} [{Level}] - {Message}";

	// 相関ID
	// スレッド（処理）ごとに開いているストリームと相関IDを保持する
	private readonly AsyncLocal<LogContext?> _currentContext = new();
	// 相関ID
	public string CorrelationId => _currentContext.Value?.CorrelationId ?? "N/A";

	// コンストラクタ
	public CustomLogger(string categoryName)
	{
		// ログカテゴリ名
		_categoryName = categoryName;

		// ファイルから設定情報を読み込む
		string configPath = 
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logsettings.json");

		if (File.Exists(configPath)) {
			try {
				using var jsonDoc = JsonDocument.Parse(File.ReadAllText(configPath));

				// ファイル読み込み
				if (jsonDoc.RootElement.TryGetProperty("LogSettings", out var logSettings) &&
					logSettings.TryGetProperty(categoryName, out var config))
				{
					// ロギングレベル
					if (config.TryGetProperty("MinimumLevel", out var lvl)) {
						_minimumLogLevel = ParseLogLevel(lvl.GetString());
					}
					// ログ出力先フォルダパス
					if (config.TryGetProperty("DirectoryPath", out var dir)) {
						_directoryPath = dir.GetString() ?? _directoryPath;
					}
					// ログ出力フォーマット
					if (config.TryGetProperty("OutputTemplate", out var tmp)) {
						_outputTemplate = tmp.GetString() ?? _outputTemplate;
					}
				}
			} catch { /* デフォルト値で動作させる */ }
		}
	}

	// BeginScope でファイルを開き、閉じるための枠組みを返す
	public IDisposable? BeginScope<TState>(TState state) 
		where TState : notnull
	{
		string correlationId = Guid.NewGuid().ToString("N")[..8];

		if (!Directory.Exists(_directoryPath)) {
			Directory.CreateDirectory(_directoryPath);
		}

		string timestampStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string fullFilePath = 
			Path.Combine(_directoryPath, $"{_categoryName}_{timestampStr}_{correlationId}.log");

		var writer = new StreamWriter(fullFilePath, append: false, Encoding.UTF8)
			{ AutoFlush = true };

		// コンテキストの生成
		_currentContext.Value = new LogContext(writer, correlationId);

		// 使い終わったら破棄するトリガー（IDisposable）を返す
		return _currentContext.Value;
	}

	public bool IsEnabled(LogLevel logLevel) 
		=> logLevel >= _minimumLogLevel;

	public void Log<TState>(
		LogLevel logLevel, EventId eventId, TState state,
		Exception? exception, Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel)) { return; }

		// 現在のコンテキスト（ファイル）がなければ書き込まない
		var context = _currentContext.Value;
		if (context is null) { return; }

		string message = formatter(state, exception);
		if (exception is not null) {
			message += $"{Environment.NewLine}{exception}";
		}

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

	private static LogLevel ParseLogLevel(string? levelStr)
		=> levelStr?.ToUpper() switch
	{
		"DEBUG" => LogLevel.Debug,
		"INFO" => LogLevel.Information,
		"WARN" => LogLevel.Warning,
		"ERROR" => LogLevel.Error,
		_ => LogLevel.Information
	};

	// ログの開閉状態とIDを管理する内部クラス
	private class LogContext : IDisposable {
		public StreamWriter Writer { get; }
		public string CorrelationId { get; }

		public LogContext(StreamWriter writer, string correlationId)
			=> (Writer, CorrelationId) = (writer, correlationId);

		public void Dispose() => Writer.Dispose();
	}
}
