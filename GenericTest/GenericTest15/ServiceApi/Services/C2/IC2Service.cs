using ServiceApi.Requests.C2;
using ServiceApi.Responses.C2;

namespace ServiceApi.Services.C2;

/*
 * Program.cs での DI 登録 (AddTransient<IA1Service, ...>)
 * に使用しているため必須です
 */
public interface IC2Service : IApiService<C2Request, C2Response> { }
