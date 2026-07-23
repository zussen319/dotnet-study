using System.Net.Http.Formatting;
using System.Web.Http;

// =============================================================================
//  フェーズ2：Web API 2 のルーティング設定
//
//  /api/{controller} の形で呼べるようにする。
//    例）/api/ping → PingController.Get()
//
//  ブラウザで開いたときに XML ではなく JSON が返るようにもしている
//  （既定ではブラウザの Accept ヘッダーにより XML が返ることがあるため）。
// =============================================================================

namespace PlmSsoDemo.Web
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // 属性ルーティングを有効化（将来 [Route] を使う場合に備える）
            config.MapHttpAttributeRoutes();

            // 規約ベースのルート
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // ブラウザで直接開いても JSON が返るようにする（動作確認しやすくするため）
            config.Formatters.JsonFormatter.SupportedMediaTypes
                  .Add(new System.Net.Http.Headers.MediaTypeHeaderValue("text/html"));

            // JSON のプロパティ名はそのまま（camelCase 変換をしない）
            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new Newtonsoft.Json.Serialization.DefaultContractResolver();

            // XML は使わない
            config.Formatters.Remove(config.Formatters.XmlFormatter);
        }
    }
}
