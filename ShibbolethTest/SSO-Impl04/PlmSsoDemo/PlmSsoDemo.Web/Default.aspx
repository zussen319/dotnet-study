<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="PlmSsoDemo.Web.Default" %>
<%--
  =============================================================================
   フェーズ4：PLM メイン画面（.aspx）

   フェーズ2からの変更点：
     ・SSO で確定した REMOTE_USER をもとに「引換券」を発行する
     ・SmartClient の起動リンクに引換券を埋め込む

   ★重要な設計点
     引換券の発行は、同じアプリ内のコードを直接呼ぶ（プロセス内）。
     HTTP でトークンサービスを呼ばないため、「X-Remote-User ヘッダーを
     信用する」という危険な作りが不要になっている。
     REMOTE_USER は SP がセットしたものを直接読んでおり、詐称できない。
  =============================================================================
--%>
<!DOCTYPE html>
<html lang="ja">
<head runat="server">
    <meta charset="utf-8" />
    <title>PLM メイン画面（フェーズ4）</title>
    <style>
        body { font-family: "Segoe UI", "Meiryo", sans-serif; margin: 2em; line-height: 1.7; }
        h1 { border-bottom: 3px solid #0a58ca; padding-bottom: .3em; }
        h2 { margin-top: 1.8em; }
        table { border-collapse: collapse; margin: 1em 0; }
        th, td { border: 1px solid #ccc; padding: 6px 12px; text-align: left; font-size: 14px; }
        th { background: #f0f4fa; white-space: nowrap; }
        td { font-family: Consolas, monospace; }
        .ok { color: #0a7d29; font-weight: bold; }
        .ng { color: #c00; font-weight: bold; }
        .warn { background: #fff8e1; border-left: 4px solid #f0ad4e; padding: 10px 14px; margin: 1em 0; }
        .note { background: #f4f4f4; border-left: 4px solid #999; padding: 10px 14px; margin: 1em 0; font-size: 14px; }
        .launch { display: inline-block; padding: 12px 22px; background: #0a58ca; color: #fff;
                  text-decoration: none; border-radius: 4px; font-size: 16px; font-weight: bold; }
        .launch:hover { background: #084298; }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        <h1>PLM メイン画面（フェーズ4）</h1>

        <asp:PlaceHolder ID="phDevWarning" runat="server" Visible="false">
            <div class="warn">
                <strong>⚠ 開発モードで動作しています。</strong><br />
                REMOTE_USER が取得できなかったため、Web.config の <code>DevRemoteUser</code> の値を使用しています。
                Shibboleth SP を経由していない状態です。<br />
                <strong>SSO の動作確認をする際は <code>DevRemoteUser</code> を削除してください。</strong>
            </div>
        </asp:PlaceHolder>

        <h2>認証状態</h2>
        <table>
            <tr><th>SSO 認証</th><td><asp:Literal ID="litAuthStatus" runat="server" /></td></tr>
            <tr><th>REMOTE_USER</th><td><asp:Literal ID="litRemoteUser" runat="server" /></td></tr>
            <tr><th>Shibboleth セッション</th><td><asp:Literal ID="litShibSession" runat="server" /></td></tr>
        </table>

        <h2>(2) SmartClient 画面</h2>
        <div class="note">
            下のリンクをクリックすると SmartClient がダウンロード・起動します。<br />
            リンクには<strong>引換券</strong>が埋め込まれており、SmartClient はそれを JWT に交換します。
            <strong>利用者はパスワードを再入力しません。</strong>
        </div>

        <p><asp:Literal ID="litLaunch" runat="server" /></p>

        <table>
            <tr><th>引換券</th><td><asp:Literal ID="litTicket" runat="server" /></td></tr>
            <tr><th>有効期限</th><td><asp:Literal ID="litTicketLife" runat="server" /></td></tr>
        </table>

        <div class="note">
            引換券は<strong>一度使うと無効</strong>になり、短時間で失効します。<br />
            もう一度起動する場合は、<strong>この画面を再読み込み</strong>して新しい引換券を取得してください。
        </div>

        <h2>(1) ブラウザ画面（.aspx）</h2>
        <p>これらは Shibboleth SP の保護下にあり、REMOTE_USER で認証されます。</p>
        <ul>
            <li><a href="<%= ResolveUrl("~/Default.aspx") %>">この画面を再読み込み（新しい引換券を取得）</a></li>
        </ul>

        <h2>診断</h2>
        <ul>
            <li><a href="<%= ResolveUrl("~/api/diag") %>" target="_blank">api/diag</a> … 設定値と内部状態の確認</li>
            <li><a href="<%= ResolveUrl("~/api/ping") %>" target="_blank">api/ping</a> … 疎通確認（SSO 保護対象外）</li>
        </ul>

        <h2>SP から渡されたサーバー変数</h2>
        <asp:Literal ID="litServerVars" runat="server" />
    </form>
</body>
</html>
