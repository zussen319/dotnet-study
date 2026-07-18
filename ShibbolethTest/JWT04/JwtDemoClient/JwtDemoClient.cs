using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// =============================================================================
//  JWT 認証デモ クライアント（ステップ3：トークンの管理）
//
//  ステップ2からの変更点：
//    ・毎回トークンを取り直すのをやめ、「1回取得 → 有効期限まで使い回す」に変更。
//    ・期限が近い/切れていれば呼ぶ前に自動で取り直す（先回り＝proactive）。
//    ・それでも 401 が返ったら取り直して1回だけ再試行する（後追い＝reactive）。
//
//  → 実際の SmartClient が内部に持つ「トークン管理」に相当する。
//  → サーバ（JwtDemoServer）はステップ1から変更なし。
// =============================================================================

// 接続先（サーバは既定の 5000 で起動している前提）
const string BaseUrl  = "http://localhost:5000";
const string Username = "01PLM01";
const string Password = "01PLM01";

using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
Console.OutputEncoding = System.Text.Encoding.UTF8;

var tokens = new TokenProvider(http, Username, Password);

Console.WriteLine("=== JWT 認証デモ クライアント（ステップ3：トークン管理）===");
Console.WriteLine($"接続先: {BaseUrl}");
Console.WriteLine();

// -----------------------------------------------------------------------------
// [1] 連続呼び出し：トークンは1回だけ取得して使い回す
// -----------------------------------------------------------------------------
Console.WriteLine("[1] whoami を3回連続で呼びます（トークンは1回だけ取得されるはず）");
for (int i = 1; i <= 3; i++)
{
    var who = await CallWhoAmIAsync(tokens);
    Console.WriteLine($"    {i}回目: {who}");
}
Console.WriteLine($"    → /token を呼んだ回数: {tokens.FetchCount}（1 が期待値＝使い回せている）");
Console.WriteLine();

// -----------------------------------------------------------------------------
// [2] 有効期限切れをシミュレート：呼ぶ前に自動で取り直す（proactive refresh）
// -----------------------------------------------------------------------------
Console.WriteLine("[2] トークンを強制的に期限切れにして呼びます（呼ぶ前に自動で取り直すはず）");
tokens.ForceExpireForDemo();
Console.WriteLine($"    結果: {await CallWhoAmIAsync(tokens)}");
Console.WriteLine($"    → /token を呼んだ回数: {tokens.FetchCount}（2 に増える＝期限前に取り直した）");
Console.WriteLine();

// -----------------------------------------------------------------------------
// [3] 401 を受けたときの取り直し（reactive refresh）
//     キャッシュを壊れたトークンに差し替え、かつ「まだ有効」と誤認させる。
//     → 先回りでは気づけないので、サーバから 401 を受けて初めて取り直す経路を試す。
// -----------------------------------------------------------------------------
Console.WriteLine("[3] キャッシュを壊れたトークンに差し替えて呼びます（401 を受けたら取り直して再試行するはず）");
tokens.InjectBrokenTokenForDemo();
Console.WriteLine($"    結果: {await CallWhoAmIAsync(tokens)}");
Console.WriteLine($"    → /token を呼んだ回数: {tokens.FetchCount}（3 に増える＝401 を受けて取り直した）");
Console.WriteLine();

Console.WriteLine("=== 完了 ===");

// =============================================================================
//  whoami を呼ぶ。401 なら1回だけ強制再取得して再試行する（reactive refresh）。
// =============================================================================
async Task<string> CallWhoAmIAsync(TokenProvider provider)
{
    for (int attempt = 1; attempt <= 2; attempt++)
    {
        var token = await provider.GetValidTokenAsync();   // 有効なら使い回し、無ければ取得

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/whoami");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await http.SendAsync(req);
        if (resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var username = body.TryGetProperty("username", out var u) ? u.GetString() : "(なし)";
            return $"成功 (200) username={username}";
        }

        if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
        {
            Console.WriteLine("        （401 を受信 → トークンを取り直して再試行します）");
            provider.Invalidate();   // 手元のトークンを捨て、次回 GetValidTokenAsync で強制再取得
            continue;
        }
        return $"拒否 ({(int)resp.StatusCode} {resp.StatusCode})";
    }
    return "拒否（再試行後も失敗）";
}

// =============================================================================
//  トークンの取得・キャッシュ・失効判定を1か所にまとめたクラス。
//  実際の SmartClient が内部に持つ「トークン管理」に相当する。
// =============================================================================
class TokenProvider
{
    private readonly HttpClient _http;
    private readonly string _user;
    private readonly string _pass;

    private string?  _token;
    private DateTime _expiresAtUtc = DateTime.MinValue;   // これを過ぎたら失効扱い

    // 期限ちょうどを避けるための安全マージン（この分だけ手前で取り直す）。
    // 時計の微妙なズレや通信の遅延で「送った瞬間に期限切れ」になるのを防ぐ。
    private static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(30);

    public int FetchCount { get; private set; }   // /token を呼んだ回数（デモ確認用）

    public TokenProvider(HttpClient http, string user, string pass)
    {
        _http = http; _user = user; _pass = pass;
    }

    // 有効なトークンを返す。無い/期限切れなら取得してキャッシュする。
    public async Task<string> GetValidTokenAsync()
    {
        if (_token is not null && DateTime.UtcNow < _expiresAtUtc)
            return _token;            // まだ有効 → 使い回す（/token は呼ばない）

        await FetchAsync();
        return _token!;
    }

    // 手元のトークンを無効化（次回 GetValidTokenAsync で強制再取得させる）。
    public void Invalidate() => _expiresAtUtc = DateTime.MinValue;

    private async Task FetchAsync()
    {
        var resp = await _http.PostAsJsonAsync("/token", new { username = _user, password = _pass });
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<TokenResponse>()
                   ?? throw new InvalidOperationException("トークン応答が空です。");

        _token = json.access_token;
        // サーバが返す expires_in（秒）から絶対期限を計算し、安全マージンを引く。
        _expiresAtUtc = DateTime.UtcNow.AddSeconds(json.expires_in) - SafetyMargin;
        FetchCount++;
        Console.WriteLine($"        （/token でトークン取得。約{json.expires_in}秒有効。取得回数={FetchCount}）");
    }

    // --- 以下はデモ用の小細工（本物の SmartClient には不要） ---

    // [2] 用：期限切れを強制する。
    public void ForceExpireForDemo() => _expiresAtUtc = DateTime.MinValue;

    // [3] 用：署名を壊したトークンをキャッシュに入れ、かつ「まだ有効」と誤認させる。
    public void InjectBrokenTokenForDemo()
    {
        if (_token is not null) _token = _token[..^2] + "xx";   // 末尾2文字を書き換えて署名を壊す
        _expiresAtUtc = DateTime.UtcNow.AddMinutes(10);         // 先回り判定では気づけない状態にする
    }
}

// レスポンス JSON（/token）に対応する型
record TokenResponse(string access_token, string token_type, int expires_in);
