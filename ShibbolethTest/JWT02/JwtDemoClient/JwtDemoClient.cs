using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// =============================================================================
//  JWT 認証デモ クライアント（ステップ2：SmartClient 役）
//
//  これまで PowerShell で手動でやっていた「トークン取得 → API 呼び出し」を、
//  プログラムで自動的に行う。実際の SmartClient（ダウンロードされて動く
//  クライアントアプリが Web サービスと通信する）の動きに近づける。
//
//  流れ：
//    (1) /token にユーザー名/パスワードを送り、JWT を受け取る
//    (2) 受け取った JWT を Authorization: Bearer に付けて /api/whoami を呼ぶ
//    (3) さらに「トークン無し」「改ざん」で 401 になることを確認する
// =============================================================================

// 接続先（サーバは既定の 5000 で起動している前提）
const string BaseUrl = "http://localhost:5000";

// このデモで使う資格情報（サーバ側の固定ユーザーに合わせる）
const string Username = "01PLM01";
const string Password = "01PLM01";

using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== JWT 認証デモ クライアント（SmartClient 役）===");
Console.WriteLine($"接続先: {BaseUrl}");
Console.WriteLine();

// -----------------------------------------------------------------------------
// (1) トークンを取得する（初回認証）
// -----------------------------------------------------------------------------
Console.WriteLine("[1] トークンを取得します（POST /token）");

string? token = await GetTokenAsync(Username, Password);
if (token is null)
{
    Console.WriteLine("  → トークン取得に失敗しました。サーバが起動しているか確認してください。");
    return;
}
Console.WriteLine($"  → 取得成功。JWT の先頭: {token[..Math.Min(40, token.Length)]}...");
Console.WriteLine();

// -----------------------------------------------------------------------------
// (2) 取得したトークンを付けて保護 API を呼ぶ
// -----------------------------------------------------------------------------
Console.WriteLine("[2] トークンを付けて保護 API を呼びます（GET /api/whoami）");
await CallWhoAmIAsync(token);
Console.WriteLine();

// -----------------------------------------------------------------------------
// (3) 検証が効いていることの確認（ここが JWT 認証の肝）
// -----------------------------------------------------------------------------
Console.WriteLine("[3] 検証が効いていることを確認します");

Console.WriteLine("  (3-a) トークン無しで呼ぶ → 401 になるはず");
await CallWhoAmIAsync(null);

Console.WriteLine("  (3-b) 改ざんしたトークンで呼ぶ → 401 になるはず");
var tampered = token[..^2] + "xx";   // 末尾2文字を書き換えて署名を壊す
await CallWhoAmIAsync(tampered);

Console.WriteLine("  (3-c) 間違ったパスワードでトークン取得 → 発行されないはず");
var badToken = await GetTokenAsync(Username, "wrong-password");
Console.WriteLine(badToken is null
    ? "        → トークンは発行されなかった（期待どおり）"
    : "        → 想定外：トークンが発行された");

Console.WriteLine();
Console.WriteLine("=== 完了 ===");


// =============================================================================
//  ヘルパー：トークン取得
// =============================================================================
async Task<string?> GetTokenAsync(string user, string pass)
{
    try
    {
        // ユーザー名/パスワードを JSON で送る
        var resp = await http.PostAsJsonAsync("/token", new { username = user, password = pass });

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            // 認証失敗（トークンは発行されない）
            return null;
        }
        resp.EnsureSuccessStatusCode();

        // 返ってきた JSON から access_token を取り出す
        var json = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        return json?.access_token;
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"  （通信エラー: {ex.Message}）");
        return null;
    }
}

// =============================================================================
//  ヘルパー：/api/whoami を呼ぶ（token が null ならトークン無しで呼ぶ）
// =============================================================================
async Task CallWhoAmIAsync(string? token)
{
    using var req = new HttpRequestMessage(HttpMethod.Get, "/api/whoami");
    if (token is not null)
    {
        // Authorization: Bearer <token> ヘッダーを付ける
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    try
    {
        var resp = await http.SendAsync(req);

        if (resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var username = body.TryGetProperty("username", out var u) ? u.GetString() : "(なし)";
            Console.WriteLine($"        → 成功 (200)。username = {username}");
        }
        else
        {
            // 401 など
            Console.WriteLine($"        → 拒否 ({(int)resp.StatusCode} {resp.StatusCode})");
        }
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"        （通信エラー: {ex.Message}）");
    }
}

// レスポンス JSON（/token）に対応する型
record TokenResponse(string access_token, string token_type, int expires_in);
