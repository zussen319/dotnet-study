using System;
using System.Collections.Generic;
using System.Deployment.Application;   // ★ClickOnce の起動情報（要：System.Deployment 参照）
using System.Drawing;
using System.Text;
using System.Windows.Forms;

// =============================================================================
//  フェーズ1 スパイク（.NET Framework 4.8 版）
//  ClickOnce の起動パラメータ受け取り検証
//
//  目的はただ一つ。
//    「ブラウザのリンク経由で ClickOnce 起動したとき、
//      URL に付けた ?ticket=XXXX がアプリに届くか」
//  を確認すること。SSO も JWT もここでは扱わない。
//
//  ★.NET Framework での取得方法（枯れた標準的な方法）
//      ApplicationDeployment.IsNetworkDeployed          … ClickOnce 起動かどうか
//      ApplicationDeployment.CurrentDeployment.ActivationUri … 起動 URL（クエリ込み）
//    ※ ActivationUri が空の場合、配置マニフェストの TrustUrlParameters が false
//       （＝「URL パラメーターの引き渡しを許可する」が未設定）の可能性が高い。
//
//  ★Visual Studio でのデバッグ実行時
//    ClickOnce 起動ではないため上記は使えない。コマンドライン引数
//    （例: --ticket=TESTTICKET）から読む。開発時はこちらを使う。
//
//  ★言語バージョンについて
//    組織の Visual Studio 2019 でもビルドできるよう、C# 7.3 の範囲で記述している。
//    （null 許容参照型・switch 式・範囲演算子などの新しい構文は使わない）
// =============================================================================

namespace SpikeClickOnce
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
        public MainForm(string[] args)
        {
            Text = "ClickOnce 引数受け取り スパイク (.NET Framework 4.8)";
            Width = 900;
            Height = 560;
            StartPosition = FormStartPosition.CenterScreen;

            TextBox box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.Dock = DockStyle.Fill;
            box.Font = new Font("Consolas", 10F);
            box.WordWrap = false;
            Controls.Add(box);

            box.Text = BuildReport(args);
        }

        private static string BuildReport(string[] args)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== ClickOnce 起動情報の確認 (.NET Framework 4.8) ===");
            sb.AppendLine();

            // -----------------------------------------------------------------
            // ClickOnce の配置情報
            // -----------------------------------------------------------------
            bool isDeployed = false;
            string activationUri = null;
            string currentVersion = null;
            bool isFirstRun = false;
            string readError = null;

            try
            {
                isDeployed = ApplicationDeployment.IsNetworkDeployed;
                if (isDeployed)
                {
                    ApplicationDeployment ad = ApplicationDeployment.CurrentDeployment;
                    // ActivationUri は TrustUrlParameters が false のとき null になる
                    activationUri = (ad.ActivationUri != null) ? ad.ActivationUri.ToString() : null;
                    currentVersion = ad.CurrentVersion.ToString();
                    isFirstRun = ad.IsFirstRun;
                }
            }
            catch (Exception ex)
            {
                readError = ex.Message;
            }

            sb.AppendLine("[ClickOnce 配置情報]");
            sb.AppendLine("  IsNetworkDeployed : " + (isDeployed ? "true" : "false"));
            sb.AppendLine("  ActivationUri     : " + Show(activationUri));
            sb.AppendLine("  CurrentVersion    : " + Show(currentVersion));
            sb.AppendLine("  IsFirstRun        : " + (isDeployed ? (isFirstRun ? "true" : "false") : "-"));
            if (readError != null)
            {
                sb.AppendLine("  （読み取り時の例外: " + readError + "）");
            }
            sb.AppendLine();

            // -----------------------------------------------------------------
            // コマンドライン引数（VS デバッグ実行時に使う）
            // -----------------------------------------------------------------
            sb.AppendLine("[コマンドライン引数]");
            if (args.Length == 0)
            {
                sb.AppendLine("  （なし）");
            }
            else
            {
                foreach (string a in args)
                {
                    sb.AppendLine("  " + a);
                }
            }
            sb.AppendLine();

            // -----------------------------------------------------------------
            // 引換券の取り出し
            // -----------------------------------------------------------------
            string source;
            string ticket = ExtractTicket(activationUri, args, out source);

            sb.AppendLine("========================================");
            if (ticket != null)
            {
                sb.AppendLine("  ★成功：ticket を受け取りました（" + source + "）");
                sb.AppendLine("  ticket = " + ticket);
            }
            else
            {
                sb.AppendLine("  ×失敗：ticket を受け取れませんでした");
            }
            sb.AppendLine("========================================");
            sb.AppendLine();

            // -----------------------------------------------------------------
            // 判定の手がかり
            // -----------------------------------------------------------------
            sb.AppendLine("[診断]");
            if (isDeployed)
            {
                sb.AppendLine("  ・ClickOnce 経由で起動されています。");

                if (string.IsNullOrEmpty(activationUri))
                {
                    sb.AppendLine("  ・しかし ActivationUri が空です。次を確認してください：");
                    sb.AppendLine("      - 発行のオプション →『マニフェスト』→");
                    sb.AppendLine("        『URL パラメーターをアプリケーションに渡すことを許可する』");
                    sb.AppendLine("      - HTTP(S) の URL から起動したか（ファイル共有からは渡せない）");
                }
                else if (activationUri.IndexOf('?') < 0)
                {
                    sb.AppendLine("  ・ActivationUri にクエリ文字列がありません。");
                    sb.AppendLine("      - リンクに ?ticket=... を付けたか");
                    sb.AppendLine("      - ブラウザが『ダウンロードしてから実行』になっていないか");
                    sb.AppendLine("        （その場合パラメータが落ちます。README の代替案を参照）");
                }
                else
                {
                    sb.AppendLine("  ・クエリ文字列を受け取れています。想定どおりの動作です。");
                }
            }
            else
            {
                sb.AppendLine("  ・ClickOnce 起動ではありません（VS からの直接実行など）。");
                sb.AppendLine("    デバッグ時は引数 --ticket=TESTTICKET を指定してください。");
            }

            sb.AppendLine();
            sb.AppendLine("[参考]");
            sb.AppendLine("  実行ファイル : " + Application.ExecutablePath);
            sb.AppendLine("  CLR バージョン: " + Environment.Version);

            return sb.ToString();
        }

        private static string Show(string v)
        {
            return string.IsNullOrEmpty(v) ? "（未設定・空）" : v;
        }

        // ActivationUri のクエリ、または引数から ticket を取り出す
        private static string ExtractTicket(string activationUri, string[] args, out string source)
        {
            // (1) ClickOnce 起動 URL から
            if (!string.IsNullOrEmpty(activationUri))
            {
                try
                {
                    Uri uri = new Uri(activationUri);
                    Dictionary<string, string> q = ParseQuery(uri.Query);
                    string t;
                    if (q.TryGetValue("ticket", out t) && !string.IsNullOrEmpty(t))
                    {
                        source = "ActivationUri のクエリ文字列";
                        return t;
                    }
                }
                catch (UriFormatException)
                {
                    // 無視して次へ
                }
            }

            // (2) コマンドライン引数から（VS デバッグ用）
            const string prefix = "--ticket=";
            foreach (string a in args)
            {
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    source = "コマンドライン引数";
                    return a.Substring(prefix.Length);
                }
            }

            source = "";
            return null;
        }

        // "?a=1&b=2" 形式を辞書にする（System.Web に依存しない簡易実装）
        private static Dictionary<string, string> ParseQuery(string query)
        {
            Dictionary<string, string> dict =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(query)) return dict;

            string trimmed = query.TrimStart('?');
            string[] pairs = trimmed.Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string pair in pairs)
            {
                int i = pair.IndexOf('=');
                if (i < 0)
                {
                    dict[Uri.UnescapeDataString(pair)] = "";
                }
                else
                {
                    string key = Uri.UnescapeDataString(pair.Substring(0, i));
                    string val = Uri.UnescapeDataString(pair.Substring(i + 1));
                    dict[key] = val;
                }
            }
            return dict;
        }
    }
}
