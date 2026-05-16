namespace ServiceApi.Resources.Messages;

/*
 * メッセージ管理クラス
 */
/*
 * リソースファイル.resxのプロパティ値が以下のようになっていること：
 * ・ビルドアクション：埋め込みリソース
 * ・カスタムツール：ResXFileCodeGenerator
 * ・カスタムツール名前空間：（正しく設定されていること）
 * ・リソースファイル.resxのアクセス修飾子 (Access Modifier)」がinternalになっていること
 */
#if true
internal class MessageResourceProvider() : ResourceBase(typeof(MessageResources))
{
    private static readonly MessageResourceProvider _instance = new();

    public static string GetMessage(string messageId) => GetMessage(messageId, []);

    public static string GetMessage(string messageId, params object[] args) =>
        $"[{messageId}] {string.Format(_instance.GetString(messageId), args)}";
}
#else
internal class MessageResourceProvider : ResourceBase
{
    private static readonly MessageResourceProvider _instance = new(typeof(MessageResources));

    private MessageResourceProvider(Type resourceType) : base(resourceType) { }

    public static string GetMessage(string messageId) => GetMessage(messageId, []);

    public static string GetMessage(string messageId, params object[] args) =>
        $"[{messageId}] {string.Format(_instance.GetString(messageId), args)}";
}
#endif
