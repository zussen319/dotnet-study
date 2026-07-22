using System;
using System.Configuration;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Hosting;

// =============================================================================
//  簡易ログ出力
//
//  ★目的
//    デバッガをアタッチしなくても、引換券の発行・引き換え・JWT の検証といった
//    処理の流れを追えるようにする。
//    ステップ1〜7の学習でサーバーのコンソールログを見ながら切り分けたのと同じ要領。
//
//  ★出力先
//    App_Data\logs\plmsso-yyyyMMdd.log
//    App_Data は既定で外部から参照できないため、ログの置き場所として適している。
//
//  ★注意
//    アプリケーションプールの ID に App_Data への「書き込み」権限が必要。
//    権限が無い場合でも例外で落とさず、黙って書き込みを諦める（本処理を止めない）。
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web.Services
{
    public static class AppLog
    {
        private static readonly object _lock = new object();

        /// <summary>ログ出力が有効か（Web.config の LogEnabled、既定 true）</summary>
        public static bool Enabled
        {
            get
            {
                string s = ConfigurationManager.AppSettings["LogEnabled"];
                if (string.IsNullOrEmpty(s)) return true;
                bool v;
                return bool.TryParse(s, out v) ? v : true;
            }
        }

        /// <summary>ログファイルの場所（診断画面での表示用）</summary>
        public static string CurrentLogPath
        {
            get
            {
                try
                {
                    return Path.Combine(GetLogDirectory(),
                        "plmsso-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                }
                catch
                {
                    return "(取得できません)";
                }
            }
        }

        /// <summary>
        /// ログを1行書く。category は "TICKET" / "JWT" / "API" など。
        /// </summary>
        public static void Write(string category, string message)
        {
            if (!Enabled) return;

            try
            {
                string dir = GetLogDirectory();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string path = Path.Combine(dir,
                    "plmsso-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

                StringBuilder sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append(" [").Append(category).Append("] ");
                sb.Append(message);

                // 呼び出し元の情報も残す（切り分けに有用）
                HttpContext ctx = HttpContext.Current;
                if (ctx != null && ctx.Request != null)
                {
                    sb.Append("  <- ").Append(ctx.Request.HttpMethod)
                      .Append(" ").Append(ctx.Request.RawUrl)
                      .Append(" from ").Append(ctx.Request.UserHostAddress);
                }

                lock (_lock)
                {
                    File.AppendAllText(path, sb.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // ログが書けなくても本処理は止めない（権限不足など）
            }
        }

        private static string GetLogDirectory()
        {
            string configured = ConfigurationManager.AppSettings["LogDirectory"];
            if (!string.IsNullOrEmpty(configured)) return configured;

            // 既定：App_Data\logs
            return HostingEnvironment.MapPath("~/App_Data/logs");
        }
    }
}
