using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;

namespace ServiceApi.Services.B1;

/*
 * Program.cs での DI 登録 (AddTransient<IA1Service, ...>)
 * に使用しているため必須です
 */
public interface IB1Service : IApiService<B1Request, B1Response> { }
