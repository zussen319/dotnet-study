using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Web.UI;
using PlmSsoDemo.Web.Services;

// =============================================================================
//  フェーズ4：メイン画面のコードビハインド
//
//  フェーズ2からの変更点：
//    ・SSO で確定した REMOTE_USER をもとに引換券を発行する
//    ・SmartClient の起動リンク（.application?ticket=...）を組み立てる
//
//  ★セキュリティ上の要点
//    引換券の発行は TicketStore.Issue() を直接呼ぶ（同一プロセス内）。
//    HTTP でトークンサービスを呼ばないため、
//    「X-Remote-User ヘッダーを信用する」という危険な作りが不要。
//    REMOTE_USER は SP がセットしたもので、クライアントからは詐称できない。
//    ここが認証の信頼の起点になる。
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web
{
    public partial class Default : Page
    {
        // ClickOnce の配置マニフェスト（/PLM/smartclient/ 配下）
        private const string SmartClientAppPath = "~/smartclient/PlmSsoDemo.SmartClient.application";

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

            phDevWarning.Visible = isDev;

            // --- 認証状態の表示 ---
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

            // --- 引換券の発行と起動リンクの組み立て ---
            BuildLaunchSection(user);

            // --- サーバー変数の一覧 ---
            litServerVars.Text = BuildServerVarsTable(ctx);
        }

        /// <summary>
        /// 引換券を発行し、SmartClient の起動リンクを組み立てる。
        /// </summary>
        private void BuildLaunchSection(string user)
        {
            if (string.IsNullOrEmpty(user))
            {
                litLaunch.Text = "<span class='ng'>認証されていないため、SmartClient は起動できません。</span>";
                litTicket.Text = "（発行なし）";
                litTicketLife.Text = "-";
                return;
            }

            try
            {
                // ★ここが橋渡しの中心：SSO で確定した利用者に対して引換券を発行する
                string ticket = TicketStore.Issue(user);

                // 起動 URL を組み立てる。
                // ★ClickOnce は絶対 URL でなくても動くが、配置マニフェストに記録された
                //   インストール URL と一致する必要があるため、アプリ相対パスで組み立てる。
                string appUrl = ResolveUrl(SmartClientAppPath);
                string launchUrl = appUrl + "?ticket=" + Server.UrlEncode(ticket);

                litLaunch.Text =
                    "<a class='launch' href='" + Server.HtmlEncode(launchUrl) + "'>" +
                    "SmartClient を起動</a>";

                // 引換券は全体を画面に出さず、先頭のみ表示（ログと同じ扱い）
                litTicket.Text = Server.HtmlEncode(TicketStore.Mask(ticket));
                litTicketLife.Text = TicketStore.LifetimeSeconds + " 秒（一度きり）";
            }
            catch (Exception ex)
            {
                AppLog.Write("PAGE", "引換券の発行に失敗: " + ex.Message);
                litLaunch.Text = "<span class='ng'>引換券の発行に失敗しました: " +
                                 Server.HtmlEncode(ex.Message) + "</span>";
                litTicket.Text = "-";
                litTicketLife.Text = "-";
            }
        }

        private string BuildServerVarsTable(HttpContext ctx)
        {
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.Append("<table>");
            sb.Append("<tr><th>変数名</th><th>値</th></tr>");

            foreach (string name in InterestingVars)
            {
                sb.Append(RenderRow(name, ctx.Request.ServerVariables[name]));
                shown.Add(name);
            }

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
