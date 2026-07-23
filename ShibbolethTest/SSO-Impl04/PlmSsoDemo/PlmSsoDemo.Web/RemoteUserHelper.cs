using System;
using System.Configuration;
using System.Web;

// =============================================================================
//  REMOTE_USER（SSO で確定した利用者）を取得する共通処理
//
//  ★信頼の起点
//    REMOTE_USER は Shibboleth SP（IIS のネイティブモジュール）がセットする。
//    クライアントからは詐称できないため、ここが認証の信頼の起点になる。
//
//  ★開発モードについて
//    Shibboleth SP は IIS Express では動かないため、Visual Studio から
//    そのままデバッグ実行すると REMOTE_USER が空になる。
//    そこで Web.config の appSettings に DevRemoteUser を設定しておくと、
//    SP が無い環境ではその値を利用者とみなす。
//
//    ！！ 本番環境では DevRemoteUser を必ず削除すること。！！
//    残したまま公開すると、誰でもその利用者になりすませる状態になる。
//
//  ★言語バージョン
//    組織の Visual Studio 2019 でもビルドできるよう C# 7.3 の範囲で記述。
// =============================================================================

namespace PlmSsoDemo.Web
{
    public static class RemoteUserHelper
    {
        /// <summary>
        /// SP がセットした REMOTE_USER を読む。
        /// ★統合パイプラインではサーバー変数コレクションが遅延構築されるため、
        ///   先に Count を参照して確実に構築させてから読み取る。
        /// </summary>
        private static string ReadRemoteUserVariable(HttpContext context)
        {
            var vars = context.Request.ServerVariables;
            int dummy = vars.Count;   // コレクションの構築を促す（値は使わない）
            return vars["REMOTE_USER"];
        }

        /// <summary>
        /// SSO で確定した利用者を返す。取得できない場合は null。
        /// </summary>
        public static string GetRemoteUser(HttpContext context)
        {
            if (context == null) return null;

            // (1) SP がセットした REMOTE_USER（本番はこれだけを使う）
            string user = ReadRemoteUserVariable(context);
            if (!string.IsNullOrEmpty(user))
            {
                return user;
            }

            // (2) 保険：ASP.NET の認証情報から
            if (context.User != null &&
                context.User.Identity != null &&
                context.User.Identity.IsAuthenticated &&
                !string.IsNullOrEmpty(context.User.Identity.Name))
            {
                return context.User.Identity.Name;
            }

            // (3) 開発モード（SP が無い環境でのみ働く）
            string dev = ConfigurationManager.AppSettings["DevRemoteUser"];
            if (!string.IsNullOrEmpty(dev))
            {
                return dev;
            }

            return null;
        }

        /// <summary>
        /// 利用者が「開発モードの値」かどうか。画面に警告を出すために使う。
        /// </summary>
        public static bool IsDevFallback(HttpContext context)
        {
            if (context == null) return false;

            string real = ReadRemoteUserVariable(context);
            if (!string.IsNullOrEmpty(real)) return false;

            string dev = ConfigurationManager.AppSettings["DevRemoteUser"];
            return !string.IsNullOrEmpty(dev);
        }

        /// <summary>
        /// Shibboleth SP のセッションが存在するか（診断用）。
        /// </summary>
        public static bool HasShibbolethSession(HttpContext context)
        {
            if (context == null) return false;
            string sid = context.Request.ServerVariables["Shib-Session-ID"];
            return !string.IsNullOrEmpty(sid);
        }
    }
}
