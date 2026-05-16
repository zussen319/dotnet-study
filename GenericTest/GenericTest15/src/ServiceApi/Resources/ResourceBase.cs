using System.Resources;

namespace ServiceApi.Resources;

/*
 * リソース管理クラス（基底）
 */
internal abstract class ResourceBase
{
    private readonly ResourceManager _resourceManager;

    // リソースクラス(.resx)の型からマネージャーを生成
    protected ResourceBase(Type resourceType) =>
        _resourceManager = new ResourceManager(resourceType);
    
    // キーを指定して文字列リソースを取得
    protected string GetString(string key)
    {
        string? value = _resourceManager.GetString(key);
        if (value is null)
        {
            throw new KeyNotFoundException($"リソースキー'{key}'が'{this.GetType().Name}'に見つかりません");
        }
        return value;
    }
}

