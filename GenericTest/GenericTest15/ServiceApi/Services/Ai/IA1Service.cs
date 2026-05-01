using ServiceApi.Requests.A1;
using ServiceApi.Responses.A1;

namespace ServiceApi.Services.A1;

/*
 * Program.cs での DI 登録 (AddTransient<IA1Service, ...>)
 * に使用しているため必須です
 */
public interface IA1Service : IApiService<A1Request, A1Response>
{
    // 追加でA1独自のメソッドが必要なければ、中身は空でOKです。
    // IApiService の Execute メソッドを継承しています。
}
