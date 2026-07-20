using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Web.UI;

// =============================================================================
//  フェーズ2：メイン画面のコードビハインド
//
//  確認したいこと：
//    ・SP 保護下の .aspx で REMOTE_USER が取得できるか
//    ・SP がどの属性をサーバー変数として渡してきているか
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web
{
    public partial class Default : Page
    {
        // 画面に出したい主要なサーバー変数（順番もこのとおりに表示）
        private static readonly string[] InterestingVars = new string[]
        {
            "REMOTE_USER",
            "AUTH_TYPE",
            "Shib-Session-ID",
            "Shib-Identity-Provider",
            "Shib-Authentication-Instant",
            "Shib-Authentication-Method",
            "Shib-Session-Index",
            "mail",
            "eppn",
            "uid",
            "persistent-id",
            "HTTPS",
            "SERVER_NAME",
            "SERVER_PORT",
            "URL",
        };

        protected void Page_Load(object sender, EventArgs e)
        {
            HttpContext ctx = HttpContext.Current;

            string user = RemoteUserHelper.GetRemoteUser(ctx);
            bool isDev = RemoteUserHelper.IsDevFallback(ctx);
            bool hasShib = RemoteUserHelper.HasShibbolethSession(ctx);

            // --- 開発モードの警告表示 ---
            phDevWarning.Visible = isDev;

            // --- 認証状態 ---
            if (string.IsNullOrEmpty(user))
            {
                litAuthStatus.Text = "<span class='ng'>✗ 未認証（REMOTE_USER を取得できません）</span>";
                litRemoteUser.Text = "<span class='ng'>（空）</span>";
            }
            else if (isDev)
            {
                litAuthStatus.Text = "<span class='ng'>△ 開発モード（SP を経由していません）</span>";
                litRemoteUser.Text = Server.HtmlEncode(user);
            }
            else
            {
                litAuthStatus.Text = "<span class='ok'>✓ SSO 認証済み</span>";
                litRemoteUser.Text = "<strong>" + Server.HtmlEncode(user) + "</strong>";
            }

            litShibSession.Text = hasShib
                ? "<span class='ok'>✓ あり</span>"
                : "<span class='ng'>✗ なし</span>";

            // --- サーバー変数の一覧 ---
            litServerVars.Text = BuildServerVarsTable(ctx);
        }

        /// <summary>
        /// 主要なサーバー変数と、名前に "shib" を含む変数をすべて表にする。
        /// </summary>
        private string BuildServerVarsTable(HttpContext ctx)
        {
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.Append("<table>");
            sb.Append("<tr><th>変数名</th><th>値</th></tr>");

            // (1) 主要な変数
            foreach (string name in InterestingVars)
            {
                string value = ctx.Request.ServerVariables[name];
                sb.Append(RenderRow(name, value));
                shown.Add(name);
            }

            // (2) 名前に "shib" を含むもの（上で出していないもの）
            foreach (string key in ctx.Request.ServerVariables.AllKeys)
            {
                if (key == null) continue;
                if (shown.Contains(key)) continue;
                if (key.IndexOf("shib", StringComparison.OrdinalIgnoreCase) < 0) continue;

                sb.Append(RenderRow(key, ctx.Request.ServerVariables[key]));
                shown.Add(key);
            }

            sb.Append("</table>");
            return sb.ToString();
        }

        private string RenderRow(string name, string value)
        {
            string display = string.IsNullOrEmpty(value)
                ? "<span style='color:#999'>（空）</span>"
                : Server.HtmlEncode(value);

            return "<tr><th>" + Server.HtmlEncode(name) + "</th><td>" + display + "</td></tr>";
        }
    }
}
