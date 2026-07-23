using System;
using System.Configuration;
using System.Net;
using System.Web.Http;
using PlmSsoDemo.Web.Services;

// =============================================================================
//  GET /PLM/api/diag
//
//  フェーズ4での追加：
//    ・署名鍵の「取得元」（資格情報マネージャー / Web.config）
//    ・アプリケーションプールの実行 ID
//    ・資格情報の名前（ターゲット）
//  を表示する。資格情報マネージャーへの移行が正しくできたかを、
//  デバッガ無しで確認できるようにするため。
//
//  ★署名鍵そのものは絶対に返さない。
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

            // --- 署名鍵の状態を確認する（値そのものは返さない）---
            string keyStatus;
            string keySource;
            try
            {
                string key = JwtHelper.SigningKey;   // 取得を実際に試す
                keyStatus = "取得できました（" +
                            System.Text.Encoding.UTF8.GetByteCount(key) + " バイト）";
                keySource = JwtHelper.LastKeySource;
            }
            catch (Exception ex)
            {
                keyStatus = "★取得できません: " + ex.Message;
                keySource = "(取得失敗)";
            }

            string target = ConfigurationManager.AppSettings["JwtSigningKeyTarget"];
            string configKey = ConfigurationManager.AppSettings["JwtSigningKey"];

            AppLog.Write("DIAG", "診断エンドポイントが呼ばれました");

            return Ok(new
            {
                status = "ok",
                serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

                // ★フェーズ4の確認ポイント
                signingKey = new
                {
                    status = keyStatus,
                    source = keySource,
                    credentialTarget = string.IsNullOrEmpty(target)
                        ? "未設定（Web.config から取得する動作）"
                        : target,
                    configKeyStillPresent = string.IsNullOrEmpty(configKey)
                        ? "なし（移行完了の状態）"
                        : "★Web.config に JwtSigningKey が残っています。移行後は削除してください"
                },

                // 資格情報は「実行しているアカウント」の領域から読まれる
                appPoolIdentity = CredentialManager.CurrentIdentityName,

                jwt = new
                {
                    issuer = JwtHelper.Issuer,
                    audience = JwtHelper.Audience,
                    lifetimeMinutes = JwtHelper.LifetimeMinutes
                },

                ticket = new
                {
                    lifetimeSeconds = TicketStore.LifetimeSeconds,
                    currentCount = TicketStore.Count
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
