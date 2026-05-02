using ServiceApi.Requests.C1;
using ServiceApi.Responses.C1;

namespace ServiceApi.Services.C1;

/*
 * Program.cs での DI 登録 (AddTransient<IA1Service, ...>)
 * に使用しているため必須です
 */
public interface IC1Service : IApiService<C1Request, C1Response>
{
}
