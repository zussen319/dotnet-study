using System;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// =============================================================================
//  JWT の発行と検証（フェーズ4：署名鍵を Windows 資格情報マネージャーから取得）
//
//  ★フェーズ3からの変更点
//    署名鍵の取得元を切り替えられるようにした。
//      ・appSettings に JwtSigningKeyTarget があれば → 資格情報マネージャーから取得
//      ・無ければ                                   → appSettings の JwtSigningKey（従来）
//
//    段階的に移行できるようにするための作りで、
//    移行が完了したら Web.config から JwtSigningKey を削除する。
//
//  ★JWT の発行・検証のロジック自体はフェーズ3から変更していない。
//    「鍵の取得元だけを変えた」ため、動作が変わらないことをもって移行成功と判断できる。
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web.Services
{
    public static class JwtHelper
    {
        /// <summary>署名鍵の取得元（診断表示用）。SigningKey を読むと更新される。</summary>
        public static string LastKeySource { get; private set; }

        /// <summary>
        /// 署名鍵を取得する。
        /// 資格情報マネージャー → Web.config の順に試す。
        /// </summary>
        public static string SigningKey
        {
            get
            {
                string key = null;
                string target = ConfigurationManager.AppSettings["JwtSigningKeyTarget"];

                // --- (1) 資格情報マネージャーから取得する ---
                if (!string.IsNullOrEmpty(target))
                {
                    string reason;
                    key = CredentialManager.Read(target, out reason);

                    if (!string.IsNullOrEmpty(key))
                    {
                        LastKeySource = "資格情報マネージャー（" + target + "）";
                    }
                    else
                    {
                        // ★取得できない場合は、黙って Web.config に戻らない。
                        //   気づかないまま平文の鍵で動き続けるのを防ぐため。
                        AppLog.Write("JWT", "署名鍵の取得に失敗: " + reason +
                                            " 実行 ID=" + CredentialManager.CurrentIdentityName);
                        throw new InvalidOperationException(
                            "資格情報マネージャーから署名鍵を取得できませんでした。" + reason +
                            "（アプリケーションプールの実行 ID: " +
                            CredentialManager.CurrentIdentityName + "）");
                    }
                }
                else
                {
                    // --- (2) 従来どおり Web.config から取得する ---
                    key = ConfigurationManager.AppSettings["JwtSigningKey"];
                    if (string.IsNullOrEmpty(key))
                        throw new InvalidOperationException(
                            "署名鍵が設定されていません。Web.config の JwtSigningKeyTarget " +
                            "（資格情報マネージャー）または JwtSigningKey（直接指定）を確認してください。");

                    LastKeySource = "Web.config の JwtSigningKey（★平文。移行前の状態）";
                }

                // HS256 は 32 バイト（256bit）以上が必要
                if (Encoding.UTF8.GetByteCount(key) < 32)
                    throw new InvalidOperationException(
                        "署名鍵が短すぎます。32 バイト以上を設定してください。" +
                        "（取得元: " + LastKeySource + "）");

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
                int v;
                string s = ConfigurationManager.AppSettings["JwtLifetimeMinutes"];
                if (!string.IsNullOrEmpty(s) && int.TryParse(s, out v) && v > 0) return v;
                return 30;
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
                new Claim("auth_via", "sso-ticket"),
            };

            SigningCredentials creds =
                new SigningCredentials(GetKey(), SecurityAlgorithms.HmacSha256);

            JwtSecurityToken token = new JwtSecurityToken(
                Issuer, Audience, claims, now, exp, creds);

            string jwt = new JwtSecurityTokenHandler().WriteToken(token);

            expiresInSeconds = LifetimeMinutes * 60;
            AppLog.Write("JWT", "発行 sub=" + subject + " 有効=" + LifetimeMinutes +
                                "分 鍵の取得元=" + LastKeySource);
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
            parameters.ValidateIssuerSigningKey = true;
            parameters.IssuerSigningKey = GetKey();
            parameters.ValidateIssuer = true;
            parameters.ValidIssuer = Issuer;
            parameters.ValidateAudience = true;
            parameters.ValidAudience = Audience;
            parameters.ValidateLifetime = true;
            parameters.ClockSkew = TimeSpan.Zero;

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
