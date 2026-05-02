using ServiceApi.Requests;
using ServiceApi.Responses;
using System.Text.Json;

namespace ServiceApi.Services;

public class TestServiceBase<TRequest, TResponse>
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    public virtual async IAsyncEnumerable<TResponse> ExecuteAsync(TRequest request)
    {
        // レスポンスデータ準備（Jsonファイルから読み込み）
        // ファイル名は"<派生テストクラス名>.json"とし、カレントフォルダに配置する
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{this.GetType().Name}.json");
        List<TResponse> responses = await LoadJsonDataAsync(filePath);

        // 検索開始前の初期遅延（クエリ実行待ちをシミュレート）
        await Task.Delay(2000);

        // 大量データを想定してループで回す
        foreach (var res in responses)
        {
            await Task.Delay(1000); // 1件ごとに少し待機
            yield return res;
        }
    }

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

            return result ?? [];
        }
        catch (Exception ex)
        {
            // エラーハンドリング（ファイル不在、JSON構文エラー、requiredメンバ欠落など）
            Console.WriteLine($"Error reading JSON: {ex.Message}");
            throw;
        }
    }
}
