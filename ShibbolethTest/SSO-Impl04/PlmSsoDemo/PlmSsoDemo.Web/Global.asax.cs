using System;
using System.Web;
using System.Web.Http;

// =============================================================================
//  フェーズ2：アプリケーション起動時の初期化
//
//  Web API のルーティングを登録する。
//  （WebForms の .aspx と Web API を1つのアプリに同居させている）
// =============================================================================

namespace PlmSsoDemo.Web
{
    public class Global : HttpApplication
    {
        protected void Application_Start(object sender, EventArgs e)
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);
        }
    }
}
