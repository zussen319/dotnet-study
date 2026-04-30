using log4net;

namespace ApiProject;

public class ApiService
{
    // config内の <logger name="ApiLogger"> を使用
    private static readonly ILog log = LogManager.GetLogger("ApiLogger");

    public List<object> Execute(int flag, string correlationId)
    {
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
        // ログスレッドコンテキストに相関IDをセット
        LogicalThreadContext.Properties["CorrelationId"] = correlationId;

        try
        {
            List<object> result = [];
            log.Info("API> Data processing started.");

            log.Info($"API> Executing query.");

            if(flag%2 == 0)
            {
                // 例外発生をシミュレート（テスト用）
                throw new Exception("exception simulation");
            }

            log.Info("API> Data processing success.");
            return result;
        }
        catch (Exception ex)
        {
            log.Error("API> Exception occurred during operation.", ex);
            throw;
        }
        finally
        {
            // 【重要】スレッドが再利用された際にIDが混ざらないよう、必ず削除する
            LogicalThreadContext.Properties.Remove("CorrelationId");

            // 【注意】ここで Close() は呼び出さない
            // プログラム全体が終了するまで api_service.log への書き込み権限を維持するため
        }
    }
}
