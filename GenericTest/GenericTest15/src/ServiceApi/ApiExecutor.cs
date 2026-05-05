using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests;
using ServiceApi.Resources.Messages;
using ServiceApi.Responses;
using ServiceApi.Services;

namespace ServiceApi;

//
// サービスの生成、実行、破棄の「ライフサイクル」を一元管理します
//
public class ApiExecutor(IServiceProvider serviceProvider) : IApiExecutor
{
    public async IAsyncEnumerable<TResponse> RunAsync<TService, TRequest, TResponse>(TRequest request)
        where TService : IApiService<TRequest, TResponse>
        where TRequest : RequestBase
        where TResponse : ResponseBase
    {
        // IAsyncEnumerable を扱うため、Scopeの寿命管理に注意が必要です
        /*
        * ## リソース管理（Scopeとusing）の安全性
        * 非同期ストリームにおいて最も難しいのは「いつDB接続を閉じるか」ですが、
        * 今回の設計はここを解決しています。
        * 1. **"using var scope"**: "ApiExecutor" 内でスコープを作っています。
        * 2. **"await foreach" の連鎖**: "Program.cs" がすべてのデータを読み終わる
        * （または途中で終了する）まで、"ApiExecutor" の "RunAsync"メソッドは
        * 「実行中」の状態を維持します。
        * 3. **自動破棄**: メインプログラム側で "await foreach" が終わると
        * 呼び出し階層を遡って "ApiExecutor" の "using scope"が抜け、
        * 最終的に "ServiceBase の "Dispose"（接続解除）が確実に実行されます。
        */
        using var scope = serviceProvider.CreateScope();

        // コンテナからインスタンスを取得
        TService service = scope.ServiceProvider.GetRequiredService<TService>();
        var enumerator = service.ExecuteAsync(request).GetAsyncEnumerator();

        // 正常終了したかどうかを管理するフラグ
        bool isCompleted = false;

        try
        {
            // 処理開始ログ出力
            Console.WriteLine(MessageResourceProvider.GetMessage(MessageId.MSG001, service.GetType().Name));

            while (true)
            {
                /*
                 * 1. 例外捕捉のタイミング：
                 * IAsyncEnumerable において、実際の重い処理（DB接続やSQL実行）が動き出すのは
                 * ExecuteAsync を呼び出した瞬間ではなく、最初の MoveNextAsync() を await した
                 * タイミングであることがほとんどです。
                 * 接続失敗 (OpenAsync): 最初の MoveNextAsync() の内部で実行されるため、
                 * 内側の catch ブロックで捕捉されます。
                 * フェッチ中の切断 (ReadAsync): 100行目であろうと1万行目であろうと、
                 * 次のデータを読みに行くのは MoveNextAsync() です。これも内側の catch がしっかり捕まえます。
                 * 
                 * 2. 捕捉の伝播：
                 * この実装では、 catch ブロックの中でログを出力した後、再度 throw; を行っています。
                 * ApiExecutor でエラーの「詳細（Oracleエラーコードなど）」を標準出力に残す。
                 * その直後に例外を再送出 (re-throw) することで、呼び出し元の Program.cs にある
                 * try-catch がそれを検知し、アプリケーション全体としての共通終了処理
                 * （エラー終了のステータスコード設定など）を行えるようになっています。
                 * 
                 * 3. クリーンアップ：
                 * ここが非同期ストリームで最も重要なポイントですが、以下の2段構えで保護されています。
                 * DisposeAsync(): 途中で例外が発生して while ループを抜けたとしても、
                 * finally ブロックが必ず実行されます。そこで enumerator.DisposeAsync() が呼ばれることで
                 * ServiceBase 側の Reader も連動して閉じられます。
                 * using var scope: ApiExecutor 自体が例外で終了する際、このスコープが破棄されます。
                 * これにより、DIコンテナが管理している Service インスタンスの Dispose() も走り、
                 * Oracleコネクションが確実に解放されます。
                 */

                TResponse response;
                try
                {
                    // NomveNextAsync (次の行の取得) の失敗を catch する
                    if (!await enumerator.MoveNextAsync()) { break; }
                    response = enumerator.Current;
                }
#if true
                catch (Exception ex)
                {
                    // エラーメッセージ生成
                    string message = ex switch
                    {
                        OracleException ox => $"[Database Error] Code: {ox.Number}, Message: {ox.Message}",
                        _ => $"[System Error] {ex.Message}"
                    };
                    Console.WriteLine(message);
                    throw;
                }
#else
                catch (OracleException ex)
                {
                    Console.WriteLine($"[Database Error] Code: {ex.Number}, Message: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[System Error] {ex.Message}");
                    throw;
                }
#endif

                // yield return は try-catch の外で行う
                /*
                 * C# では try-catch ブロックの内部で yield return を直接記述することができません
                 * （try-finally であれば可能ですが、 catch があるとコンパイルエラーになります）。
                 * これは、例外が発生した際に反復子の状態を安全に復元するのが難しいためです。
                 */
                yield return response;
            }

            isCompleted = true; // 正常に終了
            //Console.WriteLine(MessageResourceProvider.GetMessage(MessageId.MSG002));
        }
        finally
        {
            // enumerator破棄
            await enumerator.DisposeAsync();

            // 処理終了ログ出力
            string message = isCompleted
                ? MessageResourceProvider.GetMessage(MessageId.MSG002)  // 正常終了時
                : MessageResourceProvider.GetMessage(MessageId.MSG003); // 異常終了時
            Console.WriteLine(message);
        }
    }
}
