using System;
using System.Configuration;
using System.Net;
using System.Web.Http;
using PlmSsoDemo.Web.Services;

// =============================================================================
//  GET /PLM/api/diag
//
//  ★目的
//    デバッガをアタッチせずに、設定値と内部状態を確認するための診断エンドポイント。
//    「引換券が発行されているか」「署名鍵が設定されているか」「ログはどこか」を
//    ブラウザから確認できる。
//
//  ★安全のため既定では無効
//    Web.config の appSettings で DiagEnabled="true" のときだけ応答する。
//    設定値そのもの（署名鍵など）は返さず、「設定されているか」だけを返す。
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web.Controllers
{
    public class DiagController : ApiController
    {
        // GET /PLM/api/diag
        [HttpGet]
        public IHttpActionResult Get()
        {
            bool enabled;
            string s = ConfigurationManager.AppSettings["DiagEnabled"];
            if (string.IsNullOrEmpty(s) || !bool.TryParse(s, out enabled) || !enabled)
            {
                return Content(HttpStatusCode.NotFound, new
                {
                    error = "disabled",
                    message = "診断エンドポイントは無効です（Web.config の DiagEnabled）"
                });
            }

            // 署名鍵は値を返さず「設定されているか」だけを返す
            string keyStatus;
            try
            {
                string key = JwtHelper.SigningKey;
                keyStatus = "設定済み（" + System.Text.Encoding.UTF8.GetByteCount(key) + " バイト）";
            }
            catch (Exception ex)
            {
                keyStatus = "エラー: " + ex.Message;
            }

            // ログが書けるかを実際に試す
            AppLog.Write("DIAG", "診断エンドポイントが呼ばれました");

            return Ok(new
            {
                status = "ok",
                serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

                jwt = new
                {
                    signingKey = keyStatus,
                    issuer = JwtHelper.Issuer,
                    audience = JwtHelper.Audience,
                    lifetimeMinutes = JwtHelper.LifetimeMinutes
                },

                ticket = new
                {
                    lifetimeSeconds = TicketStore.LifetimeSeconds,
                    currentCount = TicketStore.Count   // 未使用の引換券の数
                },

                log = new
                {
                    enabled = AppLog.Enabled,
                    path = AppLog.CurrentLogPath
                },

                devRemoteUser = string.IsNullOrEmpty(
                    ConfigurationManager.AppSettings["DevRemoteUser"])
                    ? "未設定（正しい状態）"
                    : "★設定されています。SSO 検証時は削除してください",

                note = "この応答に署名鍵そのものは含まれません。"
            });
        }
    }
}
