using ServiceApi.Requests;
using ServiceApi.Responses;

namespace ServiceApi.Services;

/*
 * ApiExecutor の型制約
 * where TService : IApiService<TRequest, TResponse>
 * で使用しているため、必須です
 */
public interface IApiService<TRequest, TResponse>
    where TRequest : RequestBase
    where TResponse : ResponseBase, new()
{
    // Taskラップではなく、IAsyncEnumerableを直接返す
    IAsyncEnumerable<TResponse> ExecuteAsync(TRequest request);
}
