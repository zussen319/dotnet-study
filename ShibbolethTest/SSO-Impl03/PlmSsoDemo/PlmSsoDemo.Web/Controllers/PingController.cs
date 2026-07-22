using System;
using System.Web;
using System.Web.Http;

// =============================================================================
//  フェーズ2：トークンサービスの疎通確認用コントローラー
//
//  確認したいこと：
//    ・Web API 2 のルーティングが動くか（/api/ping で応答するか）
//    ・このパスが Shibboleth SP の保護対象から外れているか
//        → ブラウザのプライベートウィンドウで開いて、
//          SSO のログイン画面にリダイレクトされずに JSON が返れば成功。
//
//  フェーズ3で、このコントローラーの隣に
//    ・引換券を発行する SsoTicketController
//    ・引換券を JWT に交換する TokenController
//  を追加していく。
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web.Controllers
{
    public class PingController : ApiController
    {
        // GET /api/ping
        [HttpGet]
        public IHttpActionResult Get()
        {
            HttpContext ctx = HttpContext.Current;

            // ここで REMOTE_USER が「空であること」が正しい状態。
            // SmartClient は SSO セッションを持たずにこの API を呼ぶため、
            // このパスは SP 保護の対象外にしてある。
            string remoteUser = (ctx != null)
                ? ctx.Request.ServerVariables["REMOTE_USER"]
                : null;

            return Ok(new
            {
                status = "ok",
                message = "トークンサービス（Web API）は動作しています。",
                serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

                // 診断用：SP 保護の状態を確認するための情報
                remoteUser = string.IsNullOrEmpty(remoteUser) ? null : remoteUser,
                note = string.IsNullOrEmpty(remoteUser)
                    ? "REMOTE_USER は空です。SP 保護の対象外になっており、想定どおりです。"
                    : "REMOTE_USER が入っています。SP 保護が掛かっている可能性があります（shibboleth2.xml を確認）。"
            });
        }
    }
}
