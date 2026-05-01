namespace ServiceApi.Resources.Sql;

/*
 * リソースファイル.resxのプロパティ値が以下のようになっていること：
 * ・ビルドアクション：埋め込みリソース
 * ・カスタムツール：ResXFileCodeGenerator
 * ・カスタムツール名前空間：（正しく設定されていること）
 * ・リソースファイル.resxのアクセス修飾子 (Access Modifier)」がinternalになっていること
 */

internal class SqlResource : ResourceBase
{
    // 唯一のインスタンス
    private static readonly SqlResource _instance = new(typeof(SqlResources));

    private SqlResource(Type resourceType) : base(resourceType) { }

    // Service側からはこの static メソッドを呼ぶ
    public static string GetSql(string sqlId) => _instance.GetString(sqlId);
}
