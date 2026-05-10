using ServiceApi.Requests;
using ServiceApi.Resources.Messages;
using ServiceApi.Responses;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ServiceApi.Services;

/*
 * サービスクラス（テスト用・基底）
 */
public abstract class TestServiceBase<TRequest, TResponse>
    : IApiService<TRequest, TResponse>
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    protected TestServiceBase(string _) { }

    public virtual async IAsyncEnumerable<TResponse> ExecuteAsync(
        IEnumerable<TRequest> _,  // テストサービスではリクエストは参照しない
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // レスポンスデータ準備（Jsonファイルから読み込み）
        // ファイル名は"<派生テストクラス名>.json"とし、カレントフォルダに配置する
        string filePath = 
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{GetType().Name}.json");

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                MessageResourceProvider.GetMessage(
                    MessageId.MSG991, Path.GetFileName(filePath), "File not found."
                )
            );
        }

        // 初期遅延のシミュレーション
        // Delayにctを渡すことで、待機中に中断されても即座に終了する
        await Task.Delay(2000, ct);

        using var stream = File.OpenRead(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /*
         * JsonSerializer.DeserializeAsyncEnumerableを使うことで
         * Jsonファイルが巨大であっても読み込んだ分から即座にyield returnする
         */
        var enumerable = JsonSerializer.DeserializeAsyncEnumerable<TResponse>(stream, options, ct);

        // GetAsyncEnumerator()の戻り値をvarで受けることにより警告を回避
        await using var enumerator = enumerable.GetAsyncEnumerator(ct);

        while (true)
        {
            TResponse? response;
            try
            {
                // ctはGetAsyncEnumeratorで渡されているためMoveNextAsyncでは不要
                if (!await enumerator.MoveNextAsync()) { break; }
                response = enumerator.Current;
            }
            catch (OperationCanceledException) { throw; }  // キャンセルはそのまま投げる
            catch (JsonException jex)
            {
                // Json構文エラー（カンマ記述もれ、型違い等）をスタブ専用のメッセージでラップする
                string filename = Path.GetFileName(filePath);
                string message = MessageResourceProvider.GetMessage(MessageId.MSG991, filename, jex.Message);
                throw new InvalidOperationException(message, jex);
            }

            if (response is null) { continue; }

            await Task.Delay(1000, ct); // 待機シミュレーション（待機中に中断可能）
            yield return response;
        }
    }

    // 非同期的リソース解放
    public virtual ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
        // テスト用ベースクラスでは特に解放するものはないためCompletedTaskを返す
}
