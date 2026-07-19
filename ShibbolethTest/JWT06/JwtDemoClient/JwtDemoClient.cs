using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// =============================================================================
//  JWT 認証デモ クライアント（ステップ6：引換券で起動する SmartClient 役）
//
//  ステップ5からの変更点：
//    ・起動時に「引換券（ticket）」を受け取り、それを JWT に交換して動く。
//      → 利用者にパスワードを入力させない（SSO 済みだから）。
//    ・JWT の期限切れは、リフレッシュトークンで更新する（再起動不要）。
//
//  使い方：
//    dotnet run -- <ticket>        … 引換券で起動（本番の SmartClient 相当）
//    dotnet run                    … 引換券なし（従来のパスワード方式で比較）
//
//  ★本番では、この <ticket> は SmartClient の起動パラメータとして
//    メイン画面(.aspx)から渡される。利用者は何も入力しない。
// =============================================================================

const string BaseUrl = "http://localhost:5000";

using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("=== SmartClient 役（ステップ6：SSO からの引換券で起動）===");
Console.WriteLine($"接続先: {BaseUrl}");
Console.WriteLine();

var session = new SmartClientSession(http);

if (args.Length > 0)
{
    // ---------------------------------------------------------------------
    // 本番想定の経路：引換券を JWT に交換して起動する
    // ---------------------------------------------------------------------
    var ticket = args[0];
    Console.WriteLine($"[起動] 引換券を受け取りました: {ticket[..Math.Min(16, ticket.Length)]}...");
    Console.WriteLine("[起動] 引換券を JWT に交換します（パスワード入力なし）");

    if (!await session.ExchangeTicketAsync(ticket))
    {
        Console.WriteLine("  → 交換に失敗しました。引換券が無効・使用済み・期限切れの可能性があります。");
        return;
    }
    Console.WriteLine("  → 交換成功。JWT を取得しました。");
    Console.WriteLine();

    // 業務 API を呼ぶ
    await RunBusinessCallsAsync(session);

    // -----------------------------------------------------------------
    // 引換券が「一度きり」であることの確認
    // -----------------------------------------------------------------
    Console.WriteLine("[検証1] 同じ引換券をもう一度使ってみます（拒否されるはず）");
    var second = new SmartClientSession(http);
    Console.WriteLine(await second.ExchangeTicketAsync(ticket)
        ? "  → 想定外：2回目も交換できてしまった"
        : "  → 拒否された（期待どおり。引換券は一度きり）");
    Console.WriteLine();

    // -----------------------------------------------------------------
    // JWT が切れてもリフレッシュで継続できることの確認
    // -----------------------------------------------------------------
    Console.WriteLine("[検証2] JWT を強制的に期限切れにして API を呼びます");
    Console.WriteLine("        （リフレッシュトークンで自動更新され、再ログイン不要のはず）");
    session.ForceExpireForDemo();
    await session.CallAsync(HttpMethod.Get, "/api/whoami", showRoles: true);
    Console.WriteLine();
}
else
{
    // ---------------------------------------------------------------------
    // 比較用：従来のパスワード方式（ステップ5までと同じ）
    // ---------------------------------------------------------------------
    Console.WriteLine("[起動] 引換券が指定されていないため、パスワード方式で認証します");
    Console.WriteLine("       （本番の SmartClient ではこの入力をなくすのが目的）");
    if (!await session.LoginWithPasswordAsync("01PLM01", "01PLM01"))
    {
        Console.WriteLine("  → 認証に失敗しました。");
        return;
    }
    Console.WriteLine("  → 認証成功。");
    Console.WriteLine();

    await RunBusinessCallsAsync(session);
}

Console.WriteLine("=== 完了 ===");


// =============================================================================
//  業務 API の呼び出し（ステップ5と同じ内容）
// =============================================================================
async Task RunBusinessCallsAsync(SmartClientSession s)
{
    Console.WriteLine("[1] GET /api/whoami");
    await s.CallAsync(HttpMethod.Get, "/api/whoami", showRoles: true);

    Console.WriteLine("[2] GET /api/parts（閲覧）");
    await s.CallAsync(HttpMethod.Get, "/api/parts");

    Console.WriteLine("[3] POST /api/parts（管理者のみ）");
    await s.CallAsync(HttpMethod.Post, "/api/parts");

    Console.WriteLine();
}


// =============================================================================
//  SmartClient のセッション管理。
//  引換券／パスワードで初回のトークンを得たあとは、
//  リフレッシュトークンで JWT を更新し続ける。
// =============================================================================
class SmartClientSession
{
    private readonly HttpClient _http;

    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _expiresAtUtc = DateTime.MinValue;

    private static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(30);

    public SmartClientSession(HttpClient http) => _http = http;

    // 引換券 → JWT（本番の起動経路）
    public async Task<bool> ExchangeTicketAsync(string ticket)
    {
        var resp = await _http.PostAsJsonAsync("/token/exchange", new { ticket });
        return await StoreAsync(resp);
    }

    // ユーザー名/パスワード → JWT（比較用の従来経路）
    public async Task<bool> LoginWithPasswordAsync(string user, string pass)
    {
        var resp = await _http.PostAsJsonAsync("/token", new { username = user, password = pass });
        return await StoreAsync(resp);
    }

    // リフレッシュトークン → 新しい JWT（再ログインなしで継続）
    private async Task<bool> RefreshAsync()
    {
        if (_refreshToken is null) return false;
        Console.WriteLine("        （JWT が期限切れ → リフレッシュトークンで更新します）");
        var resp = await _http.PostAsJsonAsync("/token/refresh", new { refreshToken = _refreshToken });
        return await StoreAsync(resp);
    }

    private async Task<bool> StoreAsync(HttpResponseMessage resp)
    {
        if (!resp.IsSuccessStatusCode) return false;

        var json = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        if (json is null) return false;

        _accessToken  = json.access_token;
        _refreshToken = json.refresh_token;   // ★毎回新しい値に入れ替わる（ローテーション）
        _expiresAtUtc = DateTime.UtcNow.AddSeconds(json.expires_in) - SafetyMargin;
        return true;
    }

    // 有効な JWT を返す。切れていればリフレッシュで更新する。
    private async Task<string?> GetValidTokenAsync()
    {
        if (_accessToken is not null && DateTime.UtcNow < _expiresAtUtc)
            return _accessToken;

        return await RefreshAsync() ? _accessToken : null;
    }

    // API を呼ぶ。401 なら1回だけ更新して再試行する。
    public async Task CallAsync(HttpMethod method, string path, bool showRoles = false)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var token = await GetValidTokenAsync();
            if (token is null)
            {
                Console.WriteLine("    → トークンを取得できません（再ログインが必要）");
                return;
            }

            using var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req);

            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
                var who = body.TryGetProperty("user", out var us) ? us.GetString()
                        : body.TryGetProperty("username", out var un) ? un.GetString() : "";

                var extra = "";
                if (showRoles && body.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array)
                    extra = $" roles=[{string.Join(", ", r.EnumerateArray().Select(x => x.GetString()))}]";

                Console.WriteLine($"    → 成功 (200) user={who}{extra}");
                return;
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
            {
                _expiresAtUtc = DateTime.MinValue;   // 期限切れ扱いにして更新を促す
                continue;
            }

            var reason = resp.StatusCode switch
            {
                HttpStatusCode.Forbidden    => "権限が足りない（認証は通っている）",
                HttpStatusCode.Unauthorized => "認証されていない",
                _                           => "その他のエラー"
            };
            Console.WriteLine($"    → 拒否 ({(int)resp.StatusCode} {resp.StatusCode}) … {reason}");
            return;
        }
    }

    // デモ用：JWT を強制的に期限切れにする（リフレッシュ動作を確認するため）
    public void ForceExpireForDemo() => _expiresAtUtc = DateTime.MinValue;
}

record TokenResponse(string access_token, string token_type, int expires_in, string? refresh_token);
