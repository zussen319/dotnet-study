using System.Text;
using System.Windows.Forms;

// =============================================================================
//  フェーズ1 スパイク：ClickOnce の起動パラメータ受け取り検証
//
//  目的はただ一つ。
//    「ブラウザのリンク経由で ClickOnce 起動したとき、
//      URL に付けた ?ticket=XXXX がアプリに届くか」
//  を確認すること。JWT も SSO もここでは扱わない。
//
//  ★.NET 7 以降の取得方法
//    .NET Framework の ApplicationDeployment クラスは .NET Core 以降で使えない。
//    代わりに、ClickOnce ランチャーが環境変数で起動情報を渡してくれる。
//      ClickOnce_IsNetworkDeployed … ClickOnce 起動なら "true"
//      ClickOnce_ActivationUri     … 起動に使われた URL（クエリ文字列を含む）
//    ※ ActivationUri が空の場合、配置マニフェストの TrustUrlParameters が
//       false（＝「URL パラメーターの引き渡しを許可する」が未設定）の可能性が高い。
//
//  ★Visual Studio でのデバッグ実行時
//    ClickOnce 起動ではないため環境変数は無い。その場合はコマンドライン引数
//    （例: --ticket=TESTTICKET）から読む。開発時はこちらを使う。
// =============================================================================

namespace SpikeClickOnce;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
    }
}

public class MainForm : Form
{
    public MainForm(string[] args)
    {
        Text = "ClickOnce 引数受け取り スパイク";
        Width = 900;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

        var box = new TextBox
        {
            Multiline  = true,
            ReadOnly   = true,
            ScrollBars = ScrollBars.Vertical,
            Dock       = DockStyle.Fill,
            Font       = new Font("Consolas", 10F),
            WordWrap   = false,
        };
        Controls.Add(box);

        box.Text = BuildReport(args);
    }

    private static string BuildReport(string[] args)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== ClickOnce 起動情報の確認 ===");
        sb.AppendLine();

        // --- ClickOnce ランチャーが渡す環境変数 ---
        var isDeployed    = Environment.GetEnvironmentVariable("ClickOnce_IsNetworkDeployed");
        var activationUri = Environment.GetEnvironmentVariable("ClickOnce_ActivationUri");
        var launcherVer   = Environment.GetEnvironmentVariable("ClickOnce_LauncherVersion");
        var isFirstRun    = Environment.GetEnvironmentVariable("ClickOnce_IsFirstRun");
        var currentVer    = Environment.GetEnvironmentVariable("ClickOnce_CurrentVersion");

        sb.AppendLine("[環境変数（ClickOnce ランチャーが設定）]");
        sb.AppendLine($"  ClickOnce_IsNetworkDeployed : {Show(isDeployed)}");
        sb.AppendLine($"  ClickOnce_ActivationUri     : {Show(activationUri)}");
        sb.AppendLine($"  ClickOnce_LauncherVersion   : {Show(launcherVer)}");
        sb.AppendLine($"  ClickOnce_IsFirstRun        : {Show(isFirstRun)}");
        sb.AppendLine($"  ClickOnce_CurrentVersion    : {Show(currentVer)}");
        sb.AppendLine();

        // --- コマンドライン引数（VS デバッグ実行時に使う） ---
        sb.AppendLine("[コマンドライン引数]");
        if (args.Length == 0) sb.AppendLine("  （なし）");
        foreach (var a in args) sb.AppendLine($"  {a}");
        sb.AppendLine();

        // --- 引換券の取り出し ---
        var (ticket, source) = ExtractTicket(activationUri, args);

        sb.AppendLine("========================================");
        if (ticket is not null)
        {
            sb.AppendLine($"  ★成功：ticket を受け取りました（{source}）");
            sb.AppendLine($"  ticket = {ticket}");
        }
        else
        {
            sb.AppendLine("  ×失敗：ticket を受け取れませんでした");
        }
        sb.AppendLine("========================================");
        sb.AppendLine();

        // --- 判定の手がかり ---
        sb.AppendLine("[診断]");
        if (string.Equals(isDeployed, "true", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("  ・ClickOnce 経由で起動されています。");
            if (string.IsNullOrEmpty(activationUri))
            {
                sb.AppendLine("  ・しかし ActivationUri が空です。次を確認してください：");
                sb.AppendLine("      - 発行設定の『URL パラメーターの引き渡しを許可する』");
                sb.AppendLine("        （.pubxml の <TrustUrlParameters>true</TrustUrlParameters>）");
                sb.AppendLine("      - HTTP(S) の URL から起動したか（ファイル共有からは渡せない）");
            }
            else if (!activationUri.Contains('?'))
            {
                sb.AppendLine("  ・ActivationUri にクエリ文字列がありません。");
                sb.AppendLine("      - リンクに ?ticket=... を付けたか");
                sb.AppendLine("      - ブラウザが『ダウンロードしてから実行』になっていないか");
                sb.AppendLine("        （その場合パラメータが落ちます。README の代替案を参照）");
            }
        }
        else
        {
            sb.AppendLine("  ・ClickOnce 起動ではありません（VS からの直接実行など）。");
            sb.AppendLine("    デバッグ時は引数 --ticket=TESTTICKET を指定してください。");
        }

        sb.AppendLine();
        sb.AppendLine("[参考] 実行ファイルの場所");
        sb.AppendLine($"  {Environment.ProcessPath}");

        return sb.ToString();
    }

    private static string Show(string? v) => string.IsNullOrEmpty(v) ? "（未設定）" : v;

    // ActivationUri のクエリ、または引数から ticket を取り出す
    private static (string? ticket, string source) ExtractTicket(string? activationUri, string[] args)
    {
        // (1) ClickOnce 起動 URL から
        if (!string.IsNullOrEmpty(activationUri))
        {
            try
            {
                var uri = new Uri(activationUri);
                var t = ParseQuery(uri.Query).GetValueOrDefault("ticket");
                if (!string.IsNullOrEmpty(t)) return (t, "ClickOnce_ActivationUri のクエリ文字列");
            }
            catch (UriFormatException) { /* 無視して次へ */ }
        }

        // (2) コマンドライン引数から（VS デバッグ用）
        foreach (var a in args)
        {
            if (a.StartsWith("--ticket=", StringComparison.OrdinalIgnoreCase))
                return (a["--ticket=".Length..], "コマンドライン引数");
        }

        return (null, "");
    }

    // "?a=1&b=2" 形式を辞書にする（外部ライブラリに依存しない簡易実装）
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            if (i < 0) dict[Uri.UnescapeDataString(pair)] = "";
            else dict[Uri.UnescapeDataString(pair[..i])] = Uri.UnescapeDataString(pair[(i + 1)..]);
        }
        return dict;
    }
}
