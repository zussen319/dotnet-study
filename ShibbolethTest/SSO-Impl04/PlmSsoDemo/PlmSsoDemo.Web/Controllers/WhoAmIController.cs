using System;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Web.Http;
using PlmSsoDemo.Web.Services;

// =============================================================================
//  GET /PLM/api/whoami
//
//  JWT で保護された API。SmartClient がこれを呼べれば、
//  「SSO で認証した利用者として、業務 API を呼べる」状態が成立している。
//
//  ★.aspx 側の REMOTE_USER に対応するもの
//    ブラウザ経路：SP が REMOTE_USER を確定 → .aspx が読む
//    SmartClient  ：JWT の sub を検証して確定 → API が読む
//    どちらも最終的に同じ識別子（01PLM01@plm-lab.local）になる。
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web.Controllers
{
    public class WhoAmIController : ApiController
    {
        // GET /PLM/api/whoami
        [HttpGet]
        public IHttpActionResult Get()
        {
            // --- Authorization: Bearer <token> を取り出す ---
            string token = null;
            AuthenticationHeaderValue auth = Request.Headers.Authorization;
            if (auth != null &&
                string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                token = auth.Parameter;
            }

            if (string.IsNullOrEmpty(token))
            {
                AppLog.Write("API", "whoami 拒否: Authorization ヘッダーがありません");
                return Content(HttpStatusCode.Unauthorized, new
                {
                    error = "missing_token",
                    message = "Authorization: Bearer <token> ヘッダーが必要です"
                });
            }

            // --- JWT を検証する ---
            string reason;
            ClaimsPrincipal principal = JwtHelper.Validate(token, out reason);

            if (principal == null)
            {
                AppLog.Write("API", "whoami 拒否: " + reason);
                return Content(HttpStatusCode.Unauthorized, new
                {
                    error = "invalid_token",
                    message = reason
                });
            }

            // --- ここに来た時点で検証済み（署名・発行者・利用者・期限すべて OK）---
            string subject = JwtHelper.GetSubject(principal);
            AppLog.Write("API", "whoami 成功 sub=" + subject);

            return Ok(new
            {
                message = "JWT の検証に成功しました。",
                user = subject,
                authVia = GetClaim(principal, "auth_via"),
                tokenId = GetClaim(principal, "jti"),
                serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                note = "これは .aspx の REMOTE_USER に相当するものです（SmartClient 経路）。"
            });
        }

        private static string GetClaim(ClaimsPrincipal principal, string type)
        {
            Claim c = principal.FindFirst(type);
            return (c == null) ? null : c.Value;
        }
    }
}
