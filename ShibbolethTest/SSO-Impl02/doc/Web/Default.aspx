<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="PlmSsoDemo.Web.Default" %>
<%--
  =============================================================================
   フェーズ2：PLM メイン画面（.aspx）の最小版

   目的は「SP 保護下の .aspx から REMOTE_USER が取得できること」の確認。
   引換券の発行や SmartClient の起動はフェーズ3以降で追加する。
  =============================================================================
--%>
<!DOCTYPE html>
<html lang="ja">
<head runat="server">
    <meta charset="utf-8" />
    <title>PLM メイン画面（フェーズ2）</title>
    <style>
        body { font-family: "Segoe UI", "Meiryo", sans-serif; margin: 2em; line-height: 1.7; }
        h1 { border-bottom: 3px solid #0a58ca; padding-bottom: .3em; }
        table { border-collapse: collapse; margin: 1em 0; }
        th, td { border: 1px solid #ccc; padding: 6px 12px; text-align: left; font-size: 14px; }
        th { background: #f0f4fa; white-space: nowrap; }
        td { font-family: Consolas, monospace; }
        .ok { color: #0a7d29; font-weight: bold; }
        .ng { color: #c00; font-weight: bold; }
        .warn { background: #fff8e1; border-left: 4px solid #f0ad4e; padding: 10px 14px; margin: 1em 0; }
        .note { background: #f4f4f4; border-left: 4px solid #999; padding: 10px 14px; margin: 1em 0; font-size: 14px; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <h1>PLM メイン画面（フェーズ2）</h1>

        <%-- 開発モードの警告 --%>
        <asp:PlaceHolder ID="phDevWarning" runat="server" Visible="false">
            <div class="warn">
                <strong>⚠ 開発モードで動作しています。</strong><br />
                REMOTE_USER が取得できなかったため、Web.config の <code>DevRemoteUser</code> の値を使用しています。
                Shibboleth SP を経由していない状態です（IIS Express での実行など）。<br />
                <strong>本番環境では必ず <code>DevRemoteUser</code> を削除してください。</strong>
            </div>
        </asp:PlaceHolder>

        <h2>認証状態</h2>
        <table>
            <tr>
                <th>SSO 認証</th>
                <td><asp:Literal ID="litAuthStatus" runat="server" /></td>
            </tr>
            <tr>
                <th>REMOTE_USER</th>
                <td><asp:Literal ID="litRemoteUser" runat="server" /></td>
            </tr>
            <tr>
                <th>Shibboleth セッション</th>
                <td><asp:Literal ID="litShibSession" runat="server" /></td>
            </tr>
        </table>

        <h2>SP から渡されたサーバー変数</h2>
        <p>Shibboleth SP がセットした値の一覧です。属性連携の確認に使います。</p>
        <asp:Literal ID="litServerVars" runat="server" />

        <h2>次のフェーズへの導線（まだ機能しません）</h2>
        <div class="note">
            フェーズ3で、この画面が<strong>引換券を取得</strong>し、SmartClient の起動リンクに埋め込みます。<br />
            現時点では、疎通確認用のリンクのみ置いています。
        </div>
        <ul>
            <li><a href="<%= ResolveUrl("~/api/ping") %>" target="_blank">api/ping</a> … トークンサービス（Web API）の疎通確認。<strong>SSO 保護の対象外</strong>であることの確認も兼ねます。</li>
            <li><a href="<%= ResolveUrl("~/smartclient/launch-test.html") %>" target="_blank">smartclient/launch-test.html</a> … フェーズ1で確認した ClickOnce 起動ページ</li>
        </ul>
    </form>
</body>
</html>
