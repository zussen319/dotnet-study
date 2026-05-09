using ServiceApi.Requests;
using ServiceApi.Resources.Messages;
using ServiceApi.Responses;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ServiceApi.Services;

public abstract class TestServiceBase<TRequest, TResponse>
    : IApiService<TRequest, TResponse>
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    protected TestServiceBase(string _) { }

    public virtual async IAsyncEnumerable<TResponse> ExecuteAsync(
        IEnumerable<TRequest> _,  // リクエストは参照していない
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // レスポンスデータ準備（Jsonファイルから読み込み）
        // ファイル名は"<派生テストクラス名>.json"とし、カレントフォルダに配置する
        /*
         * BaseDirectory を使うことで、実行バイナリと同じ場所にある JSON を
         * より確実に指し示すことができます。
         */
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
        // Delayにctを渡すことで、待機中に中断されても即座に終了します
        await Task.Delay(2000, ct);

        using var stream = File.OpenRead(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /*
         * JsonSerializer.DeserializeAsyncEnumerable を使うことで、
         * JSONが巨大であっても読み込んだ分から即座に yield return できるようになり
         * 本番用サービスの挙動（ストリーム処理）により近いスタブになります。
         */
        // DeserializeAsyncEnumerableにctを渡す
        var enumerable = JsonSerializer.DeserializeAsyncEnumerable<TResponse>(stream, options, ct);

        // GetAsyncEnumerator() の戻り値を var で受けることで警告を回避
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
            catch (OperationCanceledException) { throw; } // キャンセルはそのまま投げる
            catch (JsonException jex)
            {
                // Json構文エラー（カンマ忘れ、型違い等）をスタブ専用のメッセージで包む
                string filename = Path.GetFileName(filePath);
                string message = MessageResourceProvider.GetMessage(MessageId.MSG991, filename, jex.Message);
                throw new InvalidOperationException(message, jex);
            }

            if (response is null) { continue; }

            await Task.Delay(1000, ct); // 待機シミュレーション（待機中に中断可能）
            yield return response;
        }
    }

    // IAsyncDisposable の実装
    // テスト用ベースクラスでは特に解放するものがないため、完了したTaskを返すだけでOK
    public virtual ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}
