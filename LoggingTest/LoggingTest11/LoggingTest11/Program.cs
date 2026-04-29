#if true
using ApiProject;
using log4net;
using log4net.Config;

// log4netの初期化（プログラム開始時に一度だけ実行）
XmlConfigurator.Configure(new FileInfo("log4net.config"));
ILog mainLog = LogManager.GetLogger(typeof(Program));

mainLog.Info("Main> Application start.");

var apiService = new ApiService();

try
{
    string conn = "User Id=scott;Password=tiger;Data Source=localhost:1521/xe;";
    string sql = "SELECT * FROM Employees";

    // APIを呼び出すたびに新しいIDを発行
    string correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
    mainLog.Info($"Main> Invoking API. [ID: {correlationId}]");

    var result = apiService.GetDataFromOracle(conn, sql, correlationId);

    mainLog.Info($"Main> API completed. [ID: {correlationId}]");
}
catch (Exception ex)
{
    mainLog.Error("Main> Critical error.", ex);
}

mainLog.Info("Main> Application end.");
#else

using ApiProject;
using log4net;
using log4net.Config;

// 初期化は最初の一回だけ
var logConfig = new FileInfo("log4net.config");
XmlConfigurator.Configure(logConfig);

ILog mainLog = LogManager.GetLogger(typeof(Program));

mainLog.Info("Main> Program started.");

try
{
    string connectionString = "User Id=scott;Password=tiger;Data Source=localhost:1521/xe;";
    string sql = "SELECT * FROM Employees WHERE DepartmentId = 10";

    var apiService = new ApiService();

    // API呼び出しの直前で相関ID (GUID) を生成
    string correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);

    // メイン側のログに「このIDで呼ぶ」ことを明示的に記録
    mainLog.Info($"Main> Calling API. [CorrelationID: {correlationId}]");

    // API側にもIDを渡す
    var result = apiService.GetDataFromOracle(connectionString, sql, correlationId);

    mainLog.Info($"Main> API call completed. [CorrelationID: {correlationId}]");
}
catch (Exception ex)
{
    mainLog.Error("Main> Error occurred in main process.", ex);
}

mainLog.Info("Main> Program ended.");
#endif