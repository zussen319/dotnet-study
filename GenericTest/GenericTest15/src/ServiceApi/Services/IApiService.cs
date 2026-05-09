using ServiceApi.Requests;
using ServiceApi.Responses;

namespace ServiceApi.Services;

public interface IApiService<TRequest, TResponse> : IAsyncDisposable
    where TRequest : RequestBase
    where TResponse : ResponseBase
{
    IAsyncEnumerable<TResponse> ExecuteAsync(
        IEnumerable<TRequest> requests, CancellationToken ct = default);
}