using System.Text.Json;

public class Response 
{
    /*
     * フィールド定義
     */
    public required decimal DEPTNO { get; init; }
    public string DNAME { get; init; } = string.Empty;

    public List<Emp> Employees { get; init; } = [];

    public class Emp
    {
        public required decimal EMPNO { get; init; }
        public string ENAME { get; init; } = string.Empty;
    }

    /*
     * デバッグ用
     */
    public void Print()
    {
        Console.WriteLine($"{this.DEPTNO},{this.DNAME}");
        this.Employees.ForEach(x => Console.WriteLine($"  {x.EMPNO},{x.ENAME}"));
    }

    /*
     * Jsonシリアライズ
     */
    public static async Task<List<Response>> LoadResponsesAsync(string filePath)
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
            var result = await JsonSerializer.DeserializeAsync<List<Response>>(openStream, options);

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