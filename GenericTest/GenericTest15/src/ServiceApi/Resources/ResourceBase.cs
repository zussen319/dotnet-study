using System.Resources;

namespace ServiceApi.Resources;

/*
 * リソース管理クラス（基底）
 */
internal abstract class ResourceBase(Type resourceType)
{
    private readonly ResourceManager _resourceManager = new(resourceType);

    protected string GetString(string key)
    {
        // 意図を明確にするため、例外メッセージのクラス名取得を補正
        return _resourceManager.GetString(key)
            ?? throw new KeyNotFoundException($"リソースキー'{key}'が'{GetType().Name}'に見つかりません");
    }
}
