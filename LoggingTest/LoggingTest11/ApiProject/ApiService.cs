#if true
using System.Data;
using log4net;

namespace ApiProject;

public class ApiService
{
    // config内の <logger name="ApiLogger"> を使用
    private static readonly ILog log = LogManager.GetLogger("ApiLogger");

    public DataTable GetDataFromOracle(string connectionString, string sql, string correlationId)
    {
        // 1. スレッドコンテキストにIDをセット
        /*
         * Close() のタイミングと「再呼び出し」
         * ApiService の finally ブロックで appender.Close() を呼び出しています。これには注意が必要です。
         * リスク: log4net の Appender は一度 Close すると、そのプロセス内では二度と書き込めなくなります。
         * 対策: もしメインプログラムが動いている間に「何度も API を呼び出す」可能性があるなら、
         * finally での Close() は行わないのが一般的です。
         * いつ Close すべきか: ログファイルを即座に移動・転送するなどの特殊な理由がない限り、
         * log4net に任せておけばプログラム終了時に自動で安全に閉じられます。
         * 
         * LogicalThreadContext のクリア
         * API側で LogicalThreadContext.Properties["CorrelationId"] = correlationId; を
         * セットしていますが、注意が必要です。
         * リスク: .NET のスレッドプールがスレッドを再利用するため、前の処理の GUID が残ったまま、
         * 別の無関係な処理に ID が引き継がれてしまう（ログに誤った ID が出る）可能性があります。
         * 対策: 実装例に入れた通り、必ず finally ブロックで 
         * LogicalThreadContext.Properties.Remove("CorrelationId"); を実行し、コンテキストを掃除します。
         */
        LogicalThreadContext.Properties["CorrelationId"] = correlationId;

        try
        {
            log.Info("API> Data processing started.");

            DataTable dt = new DataTable();
            // --- DBアクセスロジック（実際の実装） ---
            log.Info($"API> Executing query. Target: {sql}");

            log.Info("API> Data processing success.");
            return dt;
        }
        catch (Exception ex)
        {
            log.Error("API> Exception occurred during DB operation.", ex);
            throw;
        }
        finally
        {
            // 2. 非常に重要：スレッドが再利用された際にIDが混ざらないよう、必ず削除する
            LogicalThreadContext.Properties.Remove("CorrelationId");

            // 注意：ここで Close() は呼び出さない
            // プログラム全体が終了するまで api_service.log への書き込み権限を維持するため
        }
    }
}
#else
using System.Data;
using log4net;
using log4net.Appender;

namespace ApiProject;

public class ApiService
{
    // configで定義した名前 "ApiLogger" でロガーを取得
    private static readonly ILog log = LogManager.GetLogger("ApiLogger");

    public DataTable GetDataFromOracle(string connectionString, string sql, string correlationId)
    {
        // API側のログにIDを自動付与するための設定
        log4net.LogicalThreadContext.Properties["CorrelationId"] = correlationId;

        DataTable dt = new DataTable();
        log.Info("API> Start."); // ここから自動で ID が付与される

        try
        {
            // DB処理など
            log.Info($"API> Executing SQL: {sql}");
        }
        finally
        {
            log.Info("API> Complete.");
            // 処理終了時にコンテキストをクリア
            log4net.LogicalThreadContext.Properties.Remove("CorrelationId");
            CloseApiLog();
        }
        return dt;
    }

    private void CloseApiLog()
    {
        // このロガーに紐付いているAppenderを探して閉じる
        var logger = log.Logger as log4net.Repository.Hierarchy.Logger;
        if (logger != null)
        {
            foreach (IAppender appender in logger.Appenders)
            {
                appender.Close();
            }
        }
    }
}
#endif
