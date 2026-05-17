namespace ServiceApi.Resources.Sql;

/*
 * SQL管理クラス
 */
/*
 * リソースファイル.resxのプロパティ値が以下のようになっていること：
 * ・ビルドアクション：埋め込みリソース
 * ・カスタムツール：ResXFileCodeGenerator
 * ・カスタムツール名前空間：（正しく設定されていること）
 * ・リソースファイル.resxのアクセス修飾子 (Access Modifier)」がinternalになっていること
 */
internal class SqlResourceProvider() : ResourceBase(typeof(SqlResources))
{
    private static readonly SqlResourceProvider _instance = new();

    public static string GetSql(string sqlId) => _instance.GetString(sqlId);
}
