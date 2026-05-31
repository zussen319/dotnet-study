using Oracle.ManagedDataAccess.Client;
using ServiceApi.Common;
using ServiceApi.Requests;
using ServiceApi.Resources.Messages;
using ServiceApi.Responses;
using ServiceApi.Services;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ServiceApi;

// サービスの生成・実行・破棄のライフサイクルを管理する
public class ApiExecutor
{
    /*
     * APIのエントリポイント
     * 呼び出し元からはこのメソッドが呼び出され実行される
     * 
     * 引数順について：
     * CancellationTokenは慣習上は最後に置くことが多いが、本APIでは
     * fetchRowsより前に配置している
     * fetchRowsはAPI内部のチューニング用であり、メイン側は原則デフォルト依存
     * （指定は最終手段）
     * 一方ctはメイン側で指定する頻度が相対的に高いため、
     * 「指定頻度が高い引数を前」に置く意図でこの順序とした
     */
    [SuppressMessage("Style","IDE0008")]
    public async IAsyncEnumerable<TResponse> RunAsync<TService, TRequest, TResponse>(
            string connectionString,
            IEnumerable<TRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default,
            int fetchRows = ApiConstants.DefaultFetchRows)
            where TService : class, IApiService<TRequest, TResponse>
            where TRequest : RequestBase
            where TResponse : ResponseBase
    {
        // リクエストが0件の場合は即座に終了
        /*
         * ※requestsは「複数回列挙しても安全なコレクション（List/配列等）」を前提とする
         *   検索条件（数件〜数百件規模）であり、遅延IEnumerableは想定しないため
         *   入口での実体化（ToList）は行わない
         */
        if (requests is null || !requests.Any()) { 
            // 処理が呼び出されたことを確認できるようにするため
            // ここでは何らかのメッセージを出力すべき
            yield break;
        }

        // サービスをインスタンス化
        TService service =
            (TService)Activator.CreateInstance(typeof(TService), connectionString, fetchRows)!;

        // 正常終了を判断するためのフラグ
        bool isCompleted = false; // 未完了

        await using (service)
        {
            // 処理開始ログ出力
            Console.WriteLine(
                MessageResourceProvider.GetMessage(MessageId.MSG001, typeof(TService).Name));

            // WithCancellationでトークンを紐付けた列挙子の取得
            var enumerator =
                service.ExecuteAsync(requests, ct).WithCancellation(ct).GetAsyncEnumerator();

            try {
                while (true)
                {
                    TResponse response;
                    try
                    {
                        // 行データを取得
                        if (!await enumerator.MoveNextAsync()) { break; }
                        response = enumerator.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        // キャンセルは明示的に捕捉する
                        Console.WriteLine(MessageResourceProvider.GetMessage(MessageId.MSG005));
                        throw;
                    }
                    catch (Exception ex)
                    {
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

                    // yield returnはtry-catchの外で行う
                    /*
                     * C#ではtry-catchブロックの内部でyield returnを直接記述することができない
                     * （catch句があるとコンパイルエラーとなる）
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
