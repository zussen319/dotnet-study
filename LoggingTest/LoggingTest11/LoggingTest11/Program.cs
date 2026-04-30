using ApiProject;
using log4net;
using log4net.Config;

// log4netの初期化（プログラム開始時に一度だけ実行）
XmlConfigurator.Configure(new FileInfo("log4net.config"));
ILog mainLog = LogManager.GetLogger(typeof(Program));

mainLog.Info("Main> Application start.");

var apiService = new ApiService();

string correlationId = string.Empty;
try
{
    for (int flag = 1; flag <= 2; flag++)
    {
        // APIを呼び出すたびに新しいIDを発行
        correlationId = Guid.NewGuid().ToString("N").Substring(0, 12);
        mainLog.Info($"Main> Invoking API. [flag: {flag}, ID: {correlationId}]");

        var result = apiService.Execute(flag, correlationId);

        mainLog.Info($"Main> API completed. [flag: {flag}, ID: {correlationId}]");
    }
}
catch (Exception ex)
{
    mainLog.Error($"Main> API failed with critical error. [ID: {correlationId}]", ex);
}

mainLog.Info("Main> Application end.");
