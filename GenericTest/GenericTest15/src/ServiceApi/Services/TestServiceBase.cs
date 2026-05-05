using ServiceApi.Requests;
using ServiceApi.Resources.Messages;
using ServiceApi.Responses;
using System.Text.Json;

namespace ServiceApi.Services;

public abstract class TestServiceBase<TRequest, TResponse>
    : IApiService<TRequest, TResponse>
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    public virtual async IAsyncEnumerable<TResponse> ExecuteAsync(TRequest request)
    {
#if true
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
        await Task.Delay(2000);

        using var stream = File.OpenRead(filePath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /*
         * JsonSerializer.DeserializeAsyncEnumerable を使うことで、JSONが巨大であっても
         * 読み込んだ分から即座に yield return できるようになり、
         * 本番用サービスの挙動（ストリーム処理）により近いスタブになります。
         */
        var enumerable = JsonSerializer.DeserializeAsyncEnumerable<TResponse>(stream, options);

        // GetAsyncEnumerator() の戻り値を var で受けることで警告を回避
        await using var enumerator = enumerable.GetAsyncEnumerator();

        while (true)
        {
            TResponse? item;
            try
            {
                if (!await enumerator.MoveNextAsync()) { break; }
                item = enumerator.Current;
            }
            catch (JsonException jex)
            {
                // Json構文エラー（カンマ忘れ、型違い等）をスタブ専用のメッセージで包む
                string filename = Path.GetFileName(filePath);
                string message = MessageResourceProvider.GetMessage(MessageId.MSG991, filename, jex.Message);
                throw new InvalidOperationException(message, jex);
            }

            if (item is null) { continue; }

            await Task.Delay(1000); // 待機シミュレーション
            yield return item;
        }
#else
        // レスポンスデータ準備（Jsonファイルから読み込み）
        // ファイル名は"<派生テストクラス名>.json"とし、カレントフォルダに配置する
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{this.GetType().Name}.json");
        List<TResponse> responses = await LoadJsonDataAsync(filePath);

        // 検索開始前の初期遅延（クエリ実行待ちをシミュレート）
        await Task.Delay(2000);

        // 大量データを想定してループで回す
        foreach (var response in responses)
        {
            await Task.Delay(1000); // 1件ごとに少し待機
            yield return response;
        }
#endif
    }

#if false
/* このメソッドは使用していない */
    /*
     * Jsonシリアライズ
     */
    protected static async Task<List<TResponse>> LoadJsonDataAsync(string filePath)
    {
        // JSONのプロパティ名とC#のプロパティ名が完全一致している場合は
        // オプション指定なしでも動作しますが、大文字小文字を区別しない設定が安全です。
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        try
        {
            // ファイルをオープンして読み込み
            using FileStream openStream = File.OpenRead(filePath);

            // デシリアライズ（JSONからオブジェクトへ変換）
            // .NET 8/10では required メンバーのチェックも自動で行われます
            var result = await JsonSerializer.DeserializeAsync<List<TResponse>>(openStream, options);
            return (result is { Count: > 0 } ? result : []);
        }
        catch (Exception ex)
        {
            // エラーハンドリング（ファイル不在、JSON構文エラー、requiredメンバ欠落など）
            string filename = Path.GetFileName(filePath);
            // MSG991: Error reading Json ({0}): {1}
            string message = MessageResourceProvider.GetMessage(MessageId.MSG991, filename, ex.Message);
            throw new InvalidOperationException(message, ex);
        }
    }
#endif
}
