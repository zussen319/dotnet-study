using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests;
using ServiceApi.Resources.Messages;
using ServiceApi.Responses;
using ServiceApi.Services;
using System.Runtime.CompilerServices;

namespace ServiceApi;

//
// サービスの生成・実行・破棄のライフサイクルを管理します
//
public class ApiExecutor /* : IApiExecutor */
{
    public async IAsyncEnumerable<TResponse> RunAsync<TService, TRequest, TResponse>(
            string connectionString,
            IEnumerable<TRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
            where TService : class, IApiService<TRequest, TResponse>
            where TRequest : RequestBase
            where TResponse : ResponseBase
    {
        // リクエストが0件の場合は即座に終了
        if (requests == null || !requests.Any()) { yield break; }

        // インスタンス化
        var service = (TService)Activator.CreateInstance(typeof(TService), connectionString)!;

        // 正常終了したかどうかを管理するフラグ
        bool isCompleted = false; // 未完了

        await using (service)
        {
            // 処理開始ログ
            Console.WriteLine(MessageResourceProvider.GetMessage(MessageId.MSG001, typeof(TService).Name));

            // WithCancellationでトークンを紐付けた列挙子の取得
            var enumerator = service.ExecuteAsync(requests, ct).WithCancellation(ct).GetAsyncEnumerator();

            try
            {
                while (true)
                {
                    TResponse response;
                    try
                    {
                        // NomveNextAsync (次の行の取得) の失敗をcatchする
                        if (!await enumerator.MoveNextAsync()) { break; }
                        response = enumerator.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        // キャンセルは明示的に捕捉
                        Console.WriteLine(MessageResourceProvider.GetMessage(MessageId.MSG005));
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // エラーメッセージ生成
                        string message = ex switch
                        {
                            // OracleException
                            OracleException ox => $"[Database Error] Code: {ox.Number}, Message: {ox.Message}",
                            // その他の例外
                            _ => $"[System Error] {ex.Message}"
                        };
                        Console.WriteLine(message);
                        throw;
                    }

                    // yield return は try-catch の外で行う
                    /*
                     * C# では try-catch ブロックの内部で yield return を直接記述することができません
                     * （try-finally であれば可能ですが、 catch があるとコンパイルエラーになります）。
                     * これは、例外が発生した際に反復子の状態を安全に復元するのが難しいためです。
                     */
                    yield return response;
                }
                isCompleted = true; // 処理完了
            }
            finally
            {
                await enumerator.DisposeAsync();

                // 処理終了ログ出力
                string message = (isCompleted
                    ? MessageResourceProvider.GetMessage(MessageId.MSG002)   // 正常終了時
                    : MessageResourceProvider.GetMessage(MessageId.MSG003)); // 異常終了時
                Console.WriteLine(message);
            }
        }
    }
}
