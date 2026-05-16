using System.Text.Json;

namespace ServiceApi.Tests.Common;

public class TEST_JsonManipulator
{
    /// <summary>
    /// 指定されたJSONファイルを読み込み、指定した型のリストとして返します。
    /// </summary>
    /// <typeparam name="T">デシリアライズ先の型</typeparam>
    /// <param name="fileName">JSONファイル名（またはパス）</param>
    /// <returns>デシリアライズされたオブジェクトのリスト</returns>
    public static List<TResponse> LoadJsonData<TResponse>(string fileName)
    {
        // 実行ディレクトリからのパスを構築
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"テストデータが見つかりません: {filePath}");
        }

        string jsonString = File.ReadAllText(filePath);

        // オプション設定（プロパティ名の大文字小文字を区別しないなど、必要に応じて）
        JsonSerializerOptions options = new() {
                PropertyNameCaseInsensitive = true
            };

        return JsonSerializer.Deserialize<List<TResponse>>(jsonString, options) ?? new List<TResponse>();
    }
}
