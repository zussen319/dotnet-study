using ServiceApi.Requests;
using ServiceApi.Responses;
using System.Text.Json;

namespace ServiceApi.Services;

public class TestServiceBase<TRequest, TResponse>
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
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
