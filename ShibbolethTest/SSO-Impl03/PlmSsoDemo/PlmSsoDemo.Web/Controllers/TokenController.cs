using System.Web.Http;
using PlmSsoDemo.Web.Services;

// =============================================================================
//  POST /PLM/api/token/exchange
//
//  SmartClient が「引換券」を「JWT」に交換する。パスワードは不要。
//
//  ★このエンドポイントは SP 保護の対象外
//    SmartClient は SSO セッション（Cookie）を持たないため保護できない。
//    代わりに「有効な引換券を持っていること」が本人性の証明になる。
//    引換券は一度きり・数十秒で失効するため、漏れても被害が限定される。
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web.Controllers
{
    /// <summary>POST のボディ（JSON: { "ticket": "..." }）</summary>
    public class ExchangeRequest
    {
        public string Ticket { get; set; }
    }

    public class TokenController : ApiController
    {
        // POST /PLM/api/token/exchange
        [HttpPost]
        [ActionName("exchange")]
        public IHttpActionResult Exchange([FromBody] ExchangeRequest request)
        {
            string ticket = (request == null) ? null : request.Ticket;

            AppLog.Write("API", "交換要求 ticket=" + TicketStore.Mask(ticket));

            // --- 引換券を引き換える（一度きり）---
            string reason;
            string user = TicketStore.Consume(ticket, out reason);

            if (user == null)
            {
                // ★失敗理由を返す。デバッガが無くても切り分けられるようにするため。
                //   本番では理由を返さない方が安全な場合もあるが、
                //   引換券は短命・一度きりなので情報量は限られる。
                return Content(System.Net.HttpStatusCode.Unauthorized, new
                {
                    error = "invalid_ticket",
                    message = reason
                });
            }

            // --- JWT を発行する ---
            int expiresIn;
            string jwt = JwtHelper.Issue(user, out expiresIn);

            AppLog.Write("API", "交換成功 user=" + user);

            return Ok(new
            {
                access_token = jwt,
                token_type = "Bearer",
                expires_in = expiresIn,
                user = user
            });
        }
    }
}
