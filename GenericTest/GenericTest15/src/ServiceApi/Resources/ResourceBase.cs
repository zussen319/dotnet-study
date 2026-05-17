using System.Resources;

namespace ServiceApi.Resources;

/*
 * リソース管理クラス（基底）
 */
internal abstract class ResourceBase(Type resourceType)
{
    private readonly ResourceManager _resourceManager = new(resourceType);

    protected string GetString(string key)
        => _resourceManager.GetString(key) ??
            throw new KeyNotFoundException($"リソースキー'{key}'が'{resourceType.Name}'に見つかりません");
}
