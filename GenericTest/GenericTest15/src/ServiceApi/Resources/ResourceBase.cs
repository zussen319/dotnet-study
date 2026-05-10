using System.Resources;

namespace ServiceApi.Resources;

internal abstract class ResourceBase
{
    private readonly ResourceManager _resourceManager;

    protected ResourceBase(Type resourceType)
    {
        // リソースクラス(.resx)の型からマネージャーを生成
        _resourceManager = new ResourceManager(resourceType);
    }

    // キーを指定して文字列リソースを取得
    protected string GetString(string key)
    {
        var value = _resourceManager.GetString(key);
        if (value == null)
        {
            throw new KeyNotFoundException($"リソースキー'{key}'が'{this.GetType().Name}'に見つかりません");
        }
        return value;
    }
}

