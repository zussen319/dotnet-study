namespace ServiceApi.Resources.Messages;

/*
 * リソースファイル.resxのプロパティ値が以下のようになっていること：
 * ・ビルドアクション：埋め込みリソース
 * ・カスタムツール：ResXFileCodeGenerator
 * ・カスタムツール名前空間：（正しく設定されていること）
 * ・リソースファイル.resxのアクセス修飾子 (Access Modifier)」がinternalになっていること
 */

internal class MessageResourceProvider : ResourceBase
{
    private static readonly MessageResourceProvider _instance = new(typeof(MessageResources));

    private MessageResourceProvider(Type resourceType) : base(resourceType) { }

    public static string GetMessage(string messageId) => _instance.GetString(messageId);

    public static string GetMessage(string messageId, params object[] args) =>
        string.Format(_instance.GetString(messageId), args);
}