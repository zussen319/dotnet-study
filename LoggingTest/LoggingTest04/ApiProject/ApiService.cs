using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace ApiProject;

public class ApiService {

	// API内部だけで共有するロガーファクトリ
	private static readonly ILoggerFactory _apiLoggerFactory;
	private readonly ILogger<ApiService> _logger;

	// 静的コンストラクタで、最初に1度だけAPI専用のログ基盤を立ち上げる
	static ApiService()
	{
		// メインと同じ実行フォルダにあるappsettings.jsonをAPI側で読み込む
		var configuration = new ConfigurationBuilder()
			.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
			.AddJsonFile("logsettings.json", optional: true, reloadOnChange: true)
			.Build();

		// API専用のSerilog構成をスタンドアロンで作成
		// ※設定ファイルに「ApiSerilog」というAPI専用のセクションを作って読み込む
		var readerOptions = new Serilog.Settings.Configuration.ConfigurationReaderOptions {
			SectionName = "ApiSerilog"
		};

		var serilogLogger = new LoggerConfiguration()
			.ReadFrom.Configuration(configuration, readerOptions)
			.CreateLogger();

		// API専用の標準ロガーファクトリに変換して保持
		_apiLoggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(serilogLogger);
	}

	public ApiService()
	{
		// 自分自身のファクトリからロガーを取り出す
		_logger = _apiLoggerFactory.CreateLogger<ApiService>();
	}

	public List<object> Execute(int flag)
	{
		string correlationId = Guid.NewGuid().ToString("N")[..8];

		using (_logger.BeginScope(new Dictionary<string, object> { { "CorrelationId", correlationId } })) {
			try {
				_logger.LogInformation("API> Data processing started.");
				if (flag % 2 == 0) throw new Exception("exception simulation");
				_logger.LogInformation("API> Data processing success.");
				return [];
			} catch (Exception ex) {
				_logger.LogError(ex, "API> Exception occurred during operation.");
				throw new InvalidOperationException($"Caught API side exception [API-ID: {correlationId}]", ex);
			}
		}
	}
}
