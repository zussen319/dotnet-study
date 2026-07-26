using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Security.Cryptography;

// =============================================================================
//  引換券（チケット）の発行と引き換え
//
//  ★役割
//    SSO 認証済みの利用者（REMOTE_USER）に対して、一度きり・短命の引換券を発行する。
//    SmartClient はその引換券を JWT に交換する。
//
//  ★なぜ JWT を直接渡さないのか
//    起動パラメータや URL は、ブラウザ履歴・アクセスログ・プロセス一覧などに残りやすい。
//    JWT はそれ自体が通行証なので、拾われると有効期限まで悪用されうる。
//    引換券なら「一度使えば無効・数十秒で失効」なので、漏れても被害が限定される。
//    JWT 本体は POST の応答ボディで受け取るため、そうした場所に残らない。
//    （OAuth 2.0 の認可コードフローと同じ発想）
//
//  ★保管場所について
//    このデモではプロセス内のメモリに保管する。
//    IIS のアプリケーションプールがリサイクルされると消えるが、
//    引換券の寿命は数十秒なので実害はない。
//    Web サーバを複数台にする場合は、共有ストア（DB / Redis 等）が必要になる。
//
//  ★言語バージョン：C# 7.3 の範囲で記述（VS2019 互換）
// =============================================================================

namespace PlmSsoDemo.Web.Services
{
    public static class TicketStore
    {
        private class Entry
        {
            public string User;
            public DateTime ExpiresUtc;
        }

        private static readonly ConcurrentDictionary<string, Entry> _tickets =
            new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>引換券の有効秒数（Web.config の TicketLifetimeSeconds、既定 60 秒）</summary>
        public static int LifetimeSeconds
        {
            get
            {
#if DEBUG
				return 3600;
#else
                int v;
                string s = ConfigurationManager.AppSettings["TicketLifetimeSeconds"];
                if (!string.IsNullOrEmpty(s) && int.TryParse(s, out v) && v > 0) return v;
                return 60;
#endif
			}
		}

        /// <summary>現在保持している引換券の数（診断用）</summary>
        public static int Count { get { return _tickets.Count; } }

        /// <summary>
        /// 引換券を発行する。
        /// ★呼び出せるのは「SSO で本人確認が済んでいる」文脈だけ（Default.aspx など）。
        /// </summary>
        public static string Issue(string user)
        {
            if (string.IsNullOrEmpty(user))
                throw new ArgumentException("利用者が空です。", "user");

            CleanupExpired();

            string ticket = NewSecret();
            Entry entry = new Entry();
            entry.User = user;
            entry.ExpiresUtc = DateTime.UtcNow.AddSeconds(LifetimeSeconds);

            _tickets[ticket] = entry;

            AppLog.Write("TICKET", "発行 user=" + user +
                         " ticket=" + Mask(ticket) +
                         " 有効=" + LifetimeSeconds + "秒 保持数=" + _tickets.Count);
            return ticket;
        }

        /// <summary>
        /// 引換券を引き換える（★一度きり：取り出すと同時に削除する）。
        /// 成功なら利用者を返し、失敗なら null を返す。reason に失敗理由が入る。
        /// </summary>
        public static string Consume(string ticket, out string reason)
        {
            if (string.IsNullOrEmpty(ticket))
            {
                reason = "引換券が指定されていません";
                AppLog.Write("TICKET", "引き換え失敗: " + reason);
                return null;
            }

            Entry entry;
            // ★取り出しと削除を同時に行う。同じ券は二度使えない。
            if (!_tickets.TryRemove(ticket, out entry))
            {
                reason = "引換券が無効か、既に使用済みです";
                AppLog.Write("TICKET", "引き換え失敗: " + reason + " ticket=" + Mask(ticket));
                return null;
            }

            if (DateTime.UtcNow > entry.ExpiresUtc)
            {
                reason = "引換券の有効期限が切れています";
                AppLog.Write("TICKET", "引き換え失敗: " + reason + " ticket=" + Mask(ticket));
                return null;
            }

            reason = null;
            AppLog.Write("TICKET", "引き換え成功 user=" + entry.User +
                         " ticket=" + Mask(ticket) + " 残り保持数=" + _tickets.Count);
            return entry.User;
        }

        /// <summary>期限切れの引換券を掃除する。</summary>
        private static void CleanupExpired()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var pair in _tickets)
            {
                if (now > pair.Value.ExpiresUtc)
                {
                    Entry removed;
                    _tickets.TryRemove(pair.Key, out removed);
                }
            }
        }

        /// <summary>推測不能な引換券の値を作る（暗号論的乱数 32 バイト）。</summary>
        private static string NewSecret()
        {
            byte[] bytes = new byte[32];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(bytes);
            }
            // URL に載せるため Base64URL 形式にする（+ / = を置換・除去）
            return Convert.ToBase64String(bytes)
                          .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>ログに全体を残さないための伏せ字（先頭8文字のみ）。</summary>
        public static string Mask(string value)
        {
            if (string.IsNullOrEmpty(value)) return "(空)";
            if (value.Length <= 8) return value + "...";
            return value.Substring(0, 8) + "...";
        }
    }
}
