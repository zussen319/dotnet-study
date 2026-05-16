using System.Resources;

namespace ServiceApi.Resources;

/*
 * リソース管理クラス（基底）
 */
#if true
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
#else
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
#endif
