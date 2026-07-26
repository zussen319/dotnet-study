using System;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// =============================================================================
//  JWT の発行と検証
//
//  ★署名方式：HS256（共通鍵）
//    このアプリが発行し、このアプリが検証するため、共通鍵で十分。
//    検証する側が別サービスに分かれる段階になれば RS256（非対称鍵）を検討する。
//
//  ★署名鍵の保管
//    フェーズ3では Web.config の appSettings に固定値で置く。
//    フェーズ4で Windows 資格情報マネージャーからの取得に差し替える。
//    （一度に変えると切り分けが難しくなるため、段階を分けている）
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web.Services
{
    public static class JwtHelper
    {
        /// <summary>署名鍵。★フェーズ4でここを資格情報マネージャー参照に差し替える。</summary>
        public static string SigningKey
        {
            get
            {
                string key = ConfigurationManager.AppSettings["JwtSigningKey"];
                if (string.IsNullOrEmpty(key))
                    throw new InvalidOperationException(
                        "Web.config の appSettings に JwtSigningKey が設定されていません。");

                // HS256 は 32 バイト（256bit）以上が必要
                if (Encoding.UTF8.GetByteCount(key) < 32)
                    throw new InvalidOperationException(
                        "JwtSigningKey が短すぎます。32 バイト以上を設定してください。");

                return key;
            }
        }

        public static string Issuer
        {
            get
            {
                string v = ConfigurationManager.AppSettings["JwtIssuer"];
                return string.IsNullOrEmpty(v) ? "PlmSsoDemo" : v;
            }
        }

        public static string Audience
        {
            get
            {
                string v = ConfigurationManager.AppSettings["JwtAudience"];
                return string.IsNullOrEmpty(v) ? "PlmSmartClient" : v;
            }
        }

        /// <summary>JWT の有効分数（既定 30 分）</summary>
        public static int LifetimeMinutes
        {
            get
            {
#if DEBUG
                // デバッグ時は480分(8時間)
                return 480;
#else
                int v;
                string s = ConfigurationManager.AppSettings["JwtLifetimeMinutes"];
                if (!string.IsNullOrEmpty(s) && int.TryParse(s, out v) && v > 0) return v;
                return 30;
#endif
            }
        }

        private static SymmetricSecurityKey GetKey()
        {
            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        }

        /// <summary>
        /// 利用者を表す JWT を発行する。
        /// ★ここに来た時点で本人確認は済んでいる前提（SSO 経由の引換券を引き換えた直後）。
        /// </summary>
        public static string Issue(string subject, out int expiresInSeconds)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("subject が空です。", "subject");

            DateTime now = DateTime.UtcNow;
            DateTime exp = now.AddMinutes(LifetimeMinutes);

            Claim[] claims = new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("auth_via", "sso-ticket"),   // どの経路で得たトークンか（監査用）
            };

            SigningCredentials creds =
                new SigningCredentials(GetKey(), SecurityAlgorithms.HmacSha256);

            JwtSecurityToken token = new JwtSecurityToken(
                Issuer, Audience, claims, now, exp, creds);

            string jwt = new JwtSecurityTokenHandler().WriteToken(token);

            expiresInSeconds = LifetimeMinutes * 60;
            AppLog.Write("JWT", "発行 sub=" + subject + " 有効=" + LifetimeMinutes + "分");
            return jwt;
        }

        /// <summary>
        /// JWT を検証する。成功なら ClaimsPrincipal、失敗なら null（reason に理由）。
        /// </summary>
        public static ClaimsPrincipal Validate(string token, out string reason)
        {
            if (string.IsNullOrEmpty(token))
            {
                reason = "トークンがありません";
                return null;
            }

            TokenValidationParameters parameters = new TokenValidationParameters();
            parameters.ValidateIssuerSigningKey = true;   // 署名（改ざんの有無）
            parameters.IssuerSigningKey = GetKey();
            parameters.ValidateIssuer = true;             // 発行者
            parameters.ValidIssuer = Issuer;
            parameters.ValidateAudience = true;           // 想定利用者
            parameters.ValidAudience = Audience;
            parameters.ValidateLifetime = true;           // 有効期限
            parameters.ClockSkew = TimeSpan.Zero;         // 猶予なしで厳密に

            try
            {
                SecurityToken validated;
                ClaimsPrincipal principal =
                    new JwtSecurityTokenHandler().ValidateToken(token, parameters, out validated);
                reason = null;
                return principal;
            }
            catch (SecurityTokenExpiredException)
            {
                reason = "トークンの有効期限が切れています";
            }
            catch (SecurityTokenInvalidSignatureException)
            {
                reason = "署名が不正です（改ざんの疑い、または署名鍵の不一致）";
            }
            catch (Exception ex)
            {
                reason = "トークンの検証に失敗しました: " + ex.GetType().Name;
            }

            AppLog.Write("JWT", "検証失敗: " + reason);
            return null;
        }

        /// <summary>
        /// ClaimsPrincipal から利用者識別子（sub）を取り出す。
        /// ★.NET は標準クレーム名を URI 形式に変換することがあるため、複数候補で探す。
        ///   （ステップ1・4で実機確認した知見）
        /// </summary>
        public static string GetSubject(ClaimsPrincipal principal)
        {
            if (principal == null) return null;

            Claim c = principal.FindFirst("sub");
            if (c == null) c = principal.FindFirst(JwtRegisteredClaimNames.Sub);
            if (c == null) c = principal.FindFirst(ClaimTypes.NameIdentifier);
            if (c != null) return c.Value;

            if (principal.Identity != null && !string.IsNullOrEmpty(principal.Identity.Name))
                return principal.Identity.Name;

            return null;
        }
    }
}
