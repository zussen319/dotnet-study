using Microsoft.Extensions.Configuration;

namespace ServiceApi.Tests.Common;

internal class TEST_ConfigurationManager
{
    // 唯一のインスタンス
    private static readonly TEST_ConfigurationManager _instance = new();

    private const string configFile = "ServiceApi.Test.json";

    private readonly IConfiguration _config;

    protected TEST_ConfigurationManager()
    {
        _config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile(configFile, optional: false, reloadOnChange: true)
            .Build();
    }

    public static T GetValue<T>(string configId) {
        var value = _instance._config.GetValue<T>(configId);
        if (value is null)
        {
            throw new KeyNotFoundException($"キー '{configId}' が見つかりません");
        }
        return (T)value;
    }
}

internal static class ConfigId
{
    public const string ConnectionString = "ConnectionString";
}
