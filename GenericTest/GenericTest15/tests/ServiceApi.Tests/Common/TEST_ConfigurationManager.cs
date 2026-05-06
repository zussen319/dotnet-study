using Microsoft.Extensions.Configuration;

namespace ServiceApi.Tests.Common;

internal class TEST_ConfigurationManager
{
    // 唯一のインスタンス
    private static readonly TEST_ConfigurationManager _instance = new();

    private const string ConfigFile = "ServiceApi.Test.json";

    private readonly IConfiguration _config;

    // シングルトンを保証するためprivateとする
    private TEST_ConfigurationManager()
    {
        _config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile(ConfigFile, optional: false, reloadOnChange: true)
            .Build();
    }

    public static T GetValue<T>(string configId)
    {
        // GetSectionを使うことで、キーの存在確認をより厳密に行う
        var section = _instance._config.GetSection(configId);

        if (!section.Exists())
        {
            throw new KeyNotFoundException($"設定キー'{configId}'が'{ConfigFile}'に見つかりません。");
        }

        // Get<T>を使用。値が型変換できない場合などはnullが返る可能性がある
        var value = section.Get<T>();
        if (value is null)
        {
            throw new InvalidOperationException($"設定キー'{configId}'の値を型'{typeof(T).Name}'に変換できません。");
        }

        return value;
    }
}

internal static class ConfigId
{
    public const string ConnectionString = "ConnectionString";
}
