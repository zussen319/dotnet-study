namespace ServiceApi.Resources.Messages;

/*
 * リソースファイル.resxのプロパティ値が以下のようになっていること：
 * ・ビルドアクション：埋め込みリソース
 * ・カスタムツール：ResXFileCodeGenerator
 * ・カスタムツール名前空間：（正しく設定されていること）
 * ・リソースファイル.resxのアクセス修飾子 (Access Modifier)」がinternalになっていること
 */

internal class MessageResource : ResourceBase
{
    private static readonly MessageResource _instance = new(typeof(MessageResources));

    private MessageResource(Type resourceType) : base(resourceType) { }

    public static string GetMessage(string messageId) => _instance.GetString(messageId);

    public static string GetMessage(string messageId, params object[] args) =>
        string.Format(_instance.GetString(messageId), args);
}