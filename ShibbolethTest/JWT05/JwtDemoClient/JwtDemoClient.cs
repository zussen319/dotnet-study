using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// =============================================================================
//  JWT 認証デモ クライアント（ステップ5：ロールによる認可の確認）
//
//  ステップ3からの変更点：
//    ・2人の利用者（管理者 01PLM01／一般 01PLM02）で同じ API を呼び、
//      結果の違い（200 と 403）を並べて確認する。
//    ・トークン管理（TokenProvider）はステップ3のまま流用。
//
//  ★確認したいこと：
//     401 … 誰だか分からない（トークン無し/無効）＝認証の問題
//     403 … 誰かは分かるが権限が足りない        ＝認可の問題
// =============================================================================

const string BaseUrl = "http://localhost:5000";

using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("=== JWT 認証デモ クライアント（ステップ5：ロールによる認可）===");
Console.WriteLine($"接続先: {BaseUrl}");
Console.WriteLine();

// 2人の利用者で順に試す（Joe アカウントなのでパスワード＝ユーザー名）
foreach (var (user, note) in new[] { ("01PLM01", "管理者ロールを持つ想定"), ("01PLM02", "一般利用者の想定") })
{
    Console.WriteLine($"────────────────────────────────────────");
    Console.WriteLine($"■ {user} として実行（{note}）");
    Console.WriteLine($"────────────────────────────────────────");

    var tokens = new TokenProvider(http, user, user);

    // (1) 自分が誰で、どのロールを持っているか
    Console.WriteLine("[1] GET /api/whoami（認証のみ必要）");
    await CallAsync(tokens, HttpMethod.Get, "/api/whoami", showRoles: true);

    // (2) 一般利用者以上で閲覧できる業務データ
    Console.WriteLine("[2] GET /api/parts（plm-users または plm-admins が必要）");
    await CallAsync(tokens, HttpMethod.Get, "/api/parts");

    // (3) 管理者のみ実行できる更新操作 … ここで 200 と 403 が分かれる
    Console.WriteLine("[3] POST /api/parts（plm-admins のみ）");
    await CallAsync(tokens, HttpMethod.Post, "/api/parts");

    // (4) コード内でロール分岐 … 同じ 200 でも返る内容が変わる
    Console.WriteLine("[4] GET /api/report（ロールに応じて内容が変わる）");
    await CallAsync(tokens, HttpMethod.Get, "/api/report", showMessage: true);

    Console.WriteLine();
}

// 参考：トークン無しなら 401（認証の問題。403 とは別物）
Console.WriteLine("────────────────────────────────────────");
Console.WriteLine("■ 参考：トークン無しで呼ぶ（401 になるはず）");
using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/parts"))
{
    var resp = await http.SendAsync(req);
    Console.WriteLine($"    → {(int)resp.StatusCode} {resp.StatusCode}（誰だか分からない＝認証の問題）");
}

Console.WriteLine();
Console.WriteLine("=== 完了 ===");


// =============================================================================
//  API を呼んで結果を表示する。401 なら1回だけ取り直して再試行（ステップ3と同じ）。
// =============================================================================
async Task CallAsync(TokenProvider provider, HttpMethod method, string path,
                     bool showRoles = false, bool showMessage = false)
{
    for (int attempt = 1; attempt <= 2; attempt++)
    {
        var token = await provider.GetValidTokenAsync();

        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await http.SendAsync(req);

        if (resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var who  = body.TryGetProperty("user", out var us) ? us.GetString()
                     : body.TryGetProperty("username", out var un) ? un.GetString() : "";

            var extra = "";
            if (showRoles && body.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array)
            {
                var list = r.EnumerateArray().Select(x => x.GetString()).ToArray();
                extra = $" roles=[{(list.Length == 0 ? "(なし)" : string.Join(", ", list))}]";
            }
            if (showMessage && body.TryGetProperty("message", out var m))
                extra += $" → {m.GetString()}";

            Console.WriteLine($"    → 成功 (200) user={who}{extra}");
            return;
        }

        if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
        {
            provider.Invalidate();   // トークンを取り直して再試行
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

// =============================================================================
//  トークンの取得・キャッシュ・失効判定（ステップ3から変更なし）
// =============================================================================
class TokenProvider
{
    private readonly HttpClient _http;
    private readonly string _user;
    private readonly string _pass;

    private string?  _token;
    private DateTime _expiresAtUtc = DateTime.MinValue;

    private static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(30);

    public int FetchCount { get; private set; }

    public TokenProvider(HttpClient http, string user, string pass)
    {
        _http = http; _user = user; _pass = pass;
    }

    public async Task<string> GetValidTokenAsync()
    {
        if (_token is not null && DateTime.UtcNow < _expiresAtUtc)
            return _token;

        await FetchAsync();
        return _token!;
    }

    public void Invalidate() => _expiresAtUtc = DateTime.MinValue;

    private async Task FetchAsync()
    {
        var resp = await _http.PostAsJsonAsync("/token", new { username = _user, password = _pass });
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<TokenResponse>()
                   ?? throw new InvalidOperationException("トークン応答が空です。");

        _token = json.access_token;
        _expiresAtUtc = DateTime.UtcNow.AddSeconds(json.expires_in) - SafetyMargin;
        FetchCount++;
    }
}

record TokenResponse(string access_token, string token_type, int expires_in);
