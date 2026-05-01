using ServiceApi.Requests;
using ServiceApi.Responses;
using ServiceApi.Services;

namespace ServiceApi;

public interface IApiExecutor
{
    IAsyncEnumerable<TResponse> RunAsync<TService, TRequest, TResponse>(TRequest request)
        where TService : IApiService<TRequest, TResponse>
        where TRequest : RequestBase
        where TResponse : ResponseBase, new();
}
