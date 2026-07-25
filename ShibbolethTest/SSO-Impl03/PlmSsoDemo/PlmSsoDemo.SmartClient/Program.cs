using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;   // 要：System.Web.Extensions 参照
using System.Windows.Forms;
using System.Deployment.Application;     // 要：System.Deployment 参照

// =============================================================================
//  フェーズ3：SmartClient（引換券 → JWT → API 呼び出し）
//
//  流れ：
//    (1) 起動パラメータから引換券を受け取る（ClickOnce の ActivationUri）
//    (2) 引換券を JWT に交換する（POST /PLM/api/token/exchange）
//    (3) JWT を付けて保護 API を呼ぶ（GET /PLM/api/whoami）
//
//  ★利用者はパスワードを一切入力しない。
//    ブラウザで SSO 認証済みだからこそ、引換券が発行されている。
//
//  ★JWT の管理は最小限（要望どおり）
//    取得した JWT をメモリに保持して使うだけ。
//    有効期限が切れた場合は、ブラウザからの起動をやり直す。
//    （自動更新やリフレッシュトークンは実装しない）
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.SmartClient
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }

    public class MainForm : Form
    {
        // 接続先。ClickOnce 起動時は起動 URL から自動判定し、
        // VS デバッグ時はこの既定値を使う。
        private const string DefaultBaseUrl = "https://sp.plm-lab.local/PLM";

        private readonly TextBox _log;
        private string _baseUrl;
        private string _ticket;
        private string _accessToken;

        public MainForm(string[] args)
        {
            Text = "PLM SmartClient（フェーズ3：SSO 連携）";
            Width = 940;
            Height = 620;
            StartPosition = FormStartPosition.CenterScreen;

            _log = new TextBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.Dock = DockStyle.Fill;
            _log.Font = new Font("Consolas", 10F);
            _log.WordWrap = false;
            Controls.Add(_log);

            Shown += delegate { Run(args); };
        }

        private void Run(string[] args)
        {
            WriteLine("=== PLM SmartClient（フェーズ3）===");
            WriteLine("");

            // -----------------------------------------------------------------
            // (1) 引換券と接続先を決める
            // -----------------------------------------------------------------
            WriteLine("[1] 起動パラメータを確認します");
            string activationUri = GetActivationUri();

            _baseUrl = DetermineBaseUrl(activationUri);
            _ticket = ExtractTicket(activationUri, args);

            // ★デバッグ実行専用：引換券がどこからも得られなかった場合に限り、
            //   入力ダイアログを出す。
            //   - ClickOnce 起動やコマンドライン引数がある通常の起動では出ない
            //   - DEBUG ビルドのときだけ有効（本番の Release ビルドには含まれない）
            //   これにより、デバッグのたびにコマンドライン引数を貼り替える必要がなくなる。
#if DEBUG
            if (string.IsNullOrEmpty(_ticket))
            {
                string entered = TicketInputDialog.Ask(_baseUrl);
                if (!string.IsNullOrEmpty(entered))
                {
                    _ticket = entered.Trim();
                    WriteLine("    （デバッグ用ダイアログで引換券が入力されました）");
                }
            }
#endif

            WriteLine("    接続先   : " + _baseUrl);
            WriteLine("    引換券   : " + (string.IsNullOrEmpty(_ticket) ? "（なし）" : Mask(_ticket)));

            if (string.IsNullOrEmpty(_ticket))
            {
                WriteLine("");
                WriteLine("    ✗ 引換券がありません。");
                WriteLine("      ブラウザの PLM メイン画面から起動してください。");
                WriteLine("      （VS デバッグ時は起動引数に --ticket=... を指定します）");
                return;
            }
            WriteLine("");

            // -----------------------------------------------------------------
            // (2) 引換券を JWT に交換する
            // -----------------------------------------------------------------
            WriteLine("[2] 引換券を JWT に交換します（POST /api/token/exchange）");
            if (!ExchangeTicket())
            {
                WriteLine("");
                WriteLine("    交換に失敗しました。引換券は一度きり・短時間で失効します。");
                WriteLine("    ブラウザの画面を再読み込みして、新しい引換券で起動し直してください。");
                return;
            }
            WriteLine("");

            // -----------------------------------------------------------------
            // (3) JWT を付けて保護 API を呼ぶ
            // -----------------------------------------------------------------
            WriteLine("[3] JWT を付けて保護 API を呼びます（GET /api/whoami）");
            CallWhoAmI();
            WriteLine("");

            // -----------------------------------------------------------------
            // (4) 検証が効いていることの確認
            // -----------------------------------------------------------------
            WriteLine("[4] 検証が効いていることを確認します");

            WriteLine("  (4-a) 同じ引換券をもう一度使う → 拒否されるはず");
            string dummy;
            int status = HttpPostJson(_baseUrl + "/api/token/exchange",
                                      "{\"ticket\":\"" + _ticket + "\"}", out dummy);
            WriteLine("        → " + DescribeStatus(status) +
                      (status == 401 ? "（期待どおり。引換券は一度きり）" : "（想定外）"));

            WriteLine("  (4-b) トークン無しで API を呼ぶ → 401 になるはず");
            status = HttpGet(_baseUrl + "/api/whoami", null, out dummy);
            WriteLine("        → " + DescribeStatus(status) +
                      (status == 401 ? "（期待どおり）" : "（想定外）"));

            WriteLine("  (4-c) 改ざんしたトークンで呼ぶ → 401 になるはず");
            string tampered = _accessToken.Substring(0, _accessToken.Length - 2) + "xx";
            status = HttpGet(_baseUrl + "/api/whoami", tampered, out dummy);
            WriteLine("        → " + DescribeStatus(status) +
                      (status == 401 ? "（期待どおり。署名検証に失敗）" : "（想定外）"));

            WriteLine("");
            WriteLine("=== 完了 ===");
        }

        // =====================================================================
        //  (2) 引換券 → JWT
        // =====================================================================
        private bool ExchangeTicket()
        {
            string body = "{\"ticket\":\"" + _ticket + "\"}";
            string response;
            int status = HttpPostJson(_baseUrl + "/api/token/exchange", body, out response);

            if (status != 200)
            {
                WriteLine("    ✗ " + DescribeStatus(status));
                WriteLine("      応答: " + Shorten(response));
                return false;
            }

            try
            {
                var json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(response);
                _accessToken = GetString(json, "access_token");
                string user = GetString(json, "user");
                object expires = json.ContainsKey("expires_in") ? json["expires_in"] : null;

                if (string.IsNullOrEmpty(_accessToken))
                {
                    WriteLine("    ✗ 応答に access_token が含まれていません");
                    return false;
                }

                WriteLine("    ✓ 交換成功（パスワード入力なし）");
                WriteLine("      利用者     : " + user);
                WriteLine("      JWT        : " + Mask(_accessToken));
                WriteLine("      有効期間   : " + (expires == null ? "?" : expires.ToString()) + " 秒");
                return true;
            }
            catch (Exception ex)
            {
                WriteLine("    ✗ 応答の解析に失敗: " + ex.Message);
                return false;
            }
        }

        // =====================================================================
        //  (3) 保護 API の呼び出し
        // =====================================================================
        private void CallWhoAmI()
        {
            string response;
            int status = HttpGet(_baseUrl + "/api/whoami", _accessToken, out response);

            if (status != 200)
            {
                WriteLine("    ✗ " + DescribeStatus(status));
                WriteLine("      応答: " + Shorten(response));
                return;
            }

            try
            {
                var json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(response);
                WriteLine("    ✓ 成功（200）");
                WriteLine("      user       : " + GetString(json, "user"));
                WriteLine("      authVia    : " + GetString(json, "authVia"));
                WriteLine("      serverTime : " + GetString(json, "serverTime"));
                WriteLine("");
                WriteLine("      ★ここに表示された user は、ブラウザ画面の REMOTE_USER と同じ値です。");
                WriteLine("        SSO で認証した利用者として、API を呼べています。");
            }
            catch (Exception ex)
            {
                WriteLine("    応答の解析に失敗: " + ex.Message);
            }
        }

        // =====================================================================
        //  起動パラメータの取得
        // =====================================================================
        private static string GetActivationUri()
        {
            try
            {
                if (ApplicationDeployment.IsNetworkDeployed)
                {
                    Uri uri = ApplicationDeployment.CurrentDeployment.ActivationUri;
                    return (uri == null) ? null : uri.ToString();
                }
            }
            catch
            {
                // ClickOnce 起動でない場合など
            }
            return null;
        }

        /// <summary>
        /// 起動 URL から接続先のベース URL を求める。
        /// 例）https://sp.plm-lab.local/PLM/smartclient/xxx.application?ticket=...
        ///     → https://sp.plm-lab.local/PLM
        /// </summary>
        private static string DetermineBaseUrl(string activationUri)
        {
            if (!string.IsNullOrEmpty(activationUri))
            {
                try
                {
                    Uri uri = new Uri(activationUri);
                    string path = uri.AbsolutePath;   // /PLM/smartclient/xxx.application
                    int idx = path.LastIndexOf("/smartclient/", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string appRoot = path.Substring(0, idx);   // /PLM
                        return uri.Scheme + "://" + uri.Authority + appRoot;
                    }
                }
                catch (UriFormatException)
                {
                    // 解析できない場合は既定値へ
                }
            }
            return DefaultBaseUrl;
        }

        private static string ExtractTicket(string activationUri, string[] args)
        {
            // (1) ClickOnce 起動 URL のクエリから
            if (!string.IsNullOrEmpty(activationUri))
            {
                try
                {
                    Uri uri = new Uri(activationUri);
                    Dictionary<string, string> q = ParseQuery(uri.Query);
                    string t;
                    if (q.TryGetValue("ticket", out t) && !string.IsNullOrEmpty(t)) return t;
                }
                catch (UriFormatException) { }
            }

            // (2) コマンドライン引数から（VS デバッグ用）
            const string prefix = "--ticket=";
            foreach (string a in args)
            {
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return a.Substring(prefix.Length);
            }
            return null;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;

            string[] pairs = query.TrimStart('?').Split(new char[] { '&' },
                                                        StringSplitOptions.RemoveEmptyEntries);
            foreach (string pair in pairs)
            {
                int i = pair.IndexOf('=');
                if (i < 0) dict[Uri.UnescapeDataString(pair)] = "";
                else dict[Uri.UnescapeDataString(pair.Substring(0, i))] =
                         Uri.UnescapeDataString(pair.Substring(i + 1));
            }
            return dict;
        }

        // =====================================================================
        //  HTTP 通信（同期・シンプルに）
        // =====================================================================
        private static int HttpPostJson(string url, string jsonBody, out string response)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "application/json";

            byte[] data = Encoding.UTF8.GetBytes(jsonBody);
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream())
            {
                s.Write(data, 0, data.Length);
            }
            return ReadResponse(req, out response);
        }

        private static int HttpGet(string url, string bearerToken, out string response)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Accept = "application/json";
            if (!string.IsNullOrEmpty(bearerToken))
            {
                // ★これが「トークンをリクエストに添える」部分
                req.Headers.Add("Authorization", "Bearer " + bearerToken);
            }
            return ReadResponse(req, out response);
        }

        private static int ReadResponse(HttpWebRequest req, out string response)
        {
            try
            {
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                {
                    response = ReadAll(res);
                    return (int)res.StatusCode;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse res = ex.Response as HttpWebResponse;
                if (res != null)
                {
                    response = ReadAll(res);
                    return (int)res.StatusCode;
                }
                response = "通信エラー: " + ex.Message;
                return 0;
            }
            catch (Exception ex)
            {
                response = "エラー: " + ex.Message;
                return 0;
            }
        }

        private static string ReadAll(HttpWebResponse res)
        {
            using (Stream s = res.GetResponseStream())
            {
                if (s == null) return "";
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                {
                    return r.ReadToEnd();
                }
            }
        }

        // =====================================================================
        //  表示補助
        // =====================================================================
        private static string DescribeStatus(int status)
        {
            if (status == 0) return "通信できませんでした";
            if (status == 200) return "200 OK";
            if (status == 401) return "401 Unauthorized（認証されていない）";
            if (status == 403) return "403 Forbidden（権限不足）";
            if (status == 404) return "404 Not Found";
            return status.ToString();
        }

        private static string GetString(Dictionary<string, object> json, string key)
        {
            if (json == null || !json.ContainsKey(key) || json[key] == null) return "(なし)";
            return json[key].ToString();
        }

        private static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return "(空)";
            if (value.Length <= 12) return value + "...";
            return value.Substring(0, 12) + "...";
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(空)";
            s = s.Replace("\r", "").Replace("\n", " ");
            return (s.Length <= 200) ? s : s.Substring(0, 200) + "...";
        }

        private void WriteLine(string text)
        {
            _log.AppendText(text + Environment.NewLine);
            Application.DoEvents();
        }
    }

#if DEBUG
    // =========================================================================
    //  デバッグ実行専用：引換券を手入力するためのダイアログ
    //
    //  ★このクラスは DEBUG ビルドにのみ含まれる（#if DEBUG で囲っている）。
    //    本番の Release ビルドには一切含まれないため、配布物には影響しない。
    //
    //  使い方（デバッグ時）：
    //    1. サーバー側（w3wp.exe）にアタッチしておく
    //    2. SmartClient をデバッグ実行（コマンドライン引数は不要）
    //    3. このダイアログが出たら、ブラウザの起動リンクからコピーした
    //       引換券（ticket= の後ろの値）を貼り付けて OK
    //
    //  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
    // =========================================================================
    internal static class TicketInputDialog
    {
        /// <summary>
        /// 引換券の入力を求める。入力された値（未入力・キャンセルなら null）を返す。
        /// </summary>
        public static string Ask(string baseUrl)
        {
            Form dialog = new Form();
            dialog.Text = "デバッグ用：引換券の入力";
            dialog.Width = 640;
            dialog.Height = 230;
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MinimizeBox = false;
            dialog.MaximizeBox = false;

            Label guide = new Label();
            guide.Text =
                "ブラウザの PLM メイン画面で「SmartClient を起動」リンクを右クリックし、\r\n" +
                "「リンクのアドレスをコピー」で得た URL の、ticket= より後ろの値を貼り付けてください。\r\n" +
                "接続先: " + baseUrl + "\r\n" +
                "（このダイアログはデバッグ実行時のみ表示されます）";
            guide.SetBounds(12, 10, 600, 70);
            guide.AutoSize = false;

            TextBox input = new TextBox();
            input.SetBounds(12, 82, 600, 24);
            input.Font = new Font("Consolas", 10F);

            // URL 全体を貼っても動くよう、ticket= 以降を自動で取り出す補助
            input.Leave += delegate
            {
                string t = input.Text;
                int idx = t.IndexOf("ticket=", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string rest = t.Substring(idx + "ticket=".Length);
                    int amp = rest.IndexOf('&');
                    if (amp >= 0) rest = rest.Substring(0, amp);
                    input.Text = rest;
                }
            };

            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(438, 120, 80, 30);

            Button cancel = new Button();
            cancel.Text = "キャンセル";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(532, 120, 80, 30);

            dialog.Controls.Add(guide);
            dialog.Controls.Add(input);
            dialog.Controls.Add(ok);
            dialog.Controls.Add(cancel);
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;

            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                return input.Text;
            }
            return null;
        }
    }
#endif
}
