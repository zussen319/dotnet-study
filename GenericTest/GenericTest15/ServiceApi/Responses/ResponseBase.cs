using System.Data;

namespace ServiceApi.Responses;

public abstract class ResponseBase /* : IApiResponse */
{
    // 各クラスでマッピングロジックを強制（APIプロジェクト内限定）
    // Map()はAPI内部のみ使用に限定するため
    // IApiResponseではなくResposeBaseにinternalとして定義する
    internal abstract ResponseBase MapFromReader(IDataRecord reader);
}
