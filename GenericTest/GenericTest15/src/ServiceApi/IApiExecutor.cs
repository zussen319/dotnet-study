using ServiceApi.Requests;
using ServiceApi.Responses;
using ServiceApi.Services;

namespace ServiceApi;

public interface IApiExecutor
{
    IAsyncEnumerable<TResponse> RunAsync<TService, TRequest, TResponse>(
        string connectionString,
        IEnumerable<TRequest> requests,
        CancellationToken ct = default)
        where TService : class, IApiService<TRequest, TResponse>
        where TRequest : RequestBase
        where TResponse : ResponseBase;
}
