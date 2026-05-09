using ServiceApi.Requests;
using ServiceApi.Responses;

namespace ServiceApi.Services;

/// <summary>
/// サービスインターフェース
/// </summary>
/// <typeparam name="TRequest">リクエストクラス</typeparam>
/// <typeparam name="TResponse">レスポンスクラス</typeparam>
public interface IApiService<TRequest, TResponse> : IAsyncDisposable
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    /// <summary>
    /// サービスエントリポイント
    /// </summary>
    /// <param name="requests">リクエスト配列</param>
    /// <param name="ct">CancellationToken</param>
    /// <returns>DB検索結果</returns>
    IAsyncEnumerable<TResponse> ExecuteAsync(
        IEnumerable<TRequest> requests, CancellationToken ct = default);
}