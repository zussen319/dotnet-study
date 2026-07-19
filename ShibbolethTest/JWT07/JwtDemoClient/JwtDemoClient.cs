using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// =============================================================================
//  JWT 認証デモ クライアント（ステップ7：業務データの取得／仕上げ）
//
//  ステップ6からの変更点：
//    ・Oracle の EMP から取得した従業員データを表示する。
//    ・ロールによって「見える行数」「給与欄の有無」が変わることを確認する。
//
//  使い方：
//    dotnet run -- <ticket>    … 引換券で起動（SSO 経路。本番相当）
//    dotnet run                … パスワード方式（比較用）
// =============================================================================

const string BaseUrl = "http://localhost:5000";

using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("=== SmartClient 役（ステップ7：権限に応じた業務データ）===");
Console.WriteLine($"接続先: {BaseUrl}");
Console.WriteLine();

var session = new SmartClientSession(http);

if (args.Length > 0)
{
    var ticket = args[0];
    Console.WriteLine($"[起動] 引換券で起動します（パスワード入力なし）");
    if (!await session.ExchangeTicketAsync(ticket))
    {
        Console.WriteLine("  → 交換に失敗しました（無効・使用済み・期限切れの可能性）");
        return;
    }
    Console.WriteLine("  → JWT を取得しました。");
}
else
{
    Console.WriteLine("[起動] 引換券が無いため、パスワード方式で認証します（比較用）");
    if (!await session.LoginWithPasswordAsync("01PLM01", "01PLM01"))
    {
        Console.WriteLine("  → 認証に失敗しました。");
        return;
    }
    Console.WriteLine("  → JWT を取得しました。");
}
Console.WriteLine();

// -----------------------------------------------------------------------------
// [1] 自分が誰で、どの部門のデータが見えるのか
// -----------------------------------------------------------------------------
Console.WriteLine("[1] 自分の情報を確認します（GET /api/whoami）");
await session.ShowWhoAmIAsync();
Console.WriteLine();

// -----------------------------------------------------------------------------
// [2] ★本ステップの中心：業務データ（EMP）を取得する
// -----------------------------------------------------------------------------
Console.WriteLine("[2] 従業員データを取得します（GET /api/employees）");
await session.ShowEmployeesAsync();
Console.WriteLine();

// -----------------------------------------------------------------------------
// [3] 管理者だけが呼べる集計 API
// -----------------------------------------------------------------------------
Console.WriteLine("[3] 部門別サマリを取得します（GET /api/employees/summary・管理者のみ）");
await session.ShowSummaryAsync();
Console.WriteLine();

Console.WriteLine("=== 完了 ===");


// =============================================================================
//  SmartClient のセッション（ステップ6のトークン管理＋業務データ表示）
// =============================================================================
class SmartClientSession
{
    private readonly HttpClient _http;

    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _expiresAtUtc = DateTime.MinValue;
    private static readonly TimeSpan SafetyMargin = TimeSpan.FromSeconds(30);

    public SmartClientSession(HttpClient http) => _http = http;

    public async Task<bool> ExchangeTicketAsync(string ticket) =>
        await StoreAsync(await _http.PostAsJsonAsync("/token/exchange", new { ticket }));

    public async Task<bool> LoginWithPasswordAsync(string user, string pass) =>
        await StoreAsync(await _http.PostAsJsonAsync("/token", new { username = user, password = pass }));

    private async Task<bool> RefreshAsync()
    {
        if (_refreshToken is null) return false;
        Console.WriteLine("        （JWT が期限切れ → リフレッシュで更新します）");
        return await StoreAsync(await _http.PostAsJsonAsync("/token/refresh", new { refreshToken = _refreshToken }));
    }

    private async Task<bool> StoreAsync(HttpResponseMessage resp)
    {
        if (!resp.IsSuccessStatusCode) return false;
        var json = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        if (json is null) return false;

        _accessToken  = json.access_token;
        _refreshToken = json.refresh_token;
        _expiresAtUtc = DateTime.UtcNow.AddSeconds(json.expires_in) - SafetyMargin;
        return true;
    }

    private async Task<string?> GetValidTokenAsync()
    {
        if (_accessToken is not null && DateTime.UtcNow < _expiresAtUtc) return _accessToken;
        return await RefreshAsync() ? _accessToken : null;
    }

    // API を呼び、成功なら JSON を返す。401 なら1回だけ更新して再試行。
    private async Task<JsonElement?> GetJsonAsync(string path)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var token = await GetValidTokenAsync();
            if (token is null)
            {
                Console.WriteLine("    → トークンを取得できません（再ログインが必要）");
                return null;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await _http.SendAsync(req);

            if (resp.IsSuccessStatusCode)
                return await resp.Content.ReadFromJsonAsync<JsonElement>();

            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
            {
                _expiresAtUtc = DateTime.MinValue;
                continue;
            }

            var reason = resp.StatusCode switch
            {
                HttpStatusCode.Forbidden    => "権限が足りない（認証は通っている）",
                HttpStatusCode.Unauthorized => "認証されていない",
                _                           => "その他のエラー"
            };
            Console.WriteLine($"    → 拒否 ({(int)resp.StatusCode} {resp.StatusCode}) … {reason}");
            return null;
        }
        return null;
    }

    public async Task ShowWhoAmIAsync()
    {
        var body = await GetJsonAsync("/api/whoami");
        if (body is null) return;
        var b = body.Value;

        var roles = b.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array
            ? string.Join(", ", r.EnumerateArray().Select(x => x.GetString()))
            : "(なし)";
        var dept = b.TryGetProperty("department", out var d) && d.ValueKind == JsonValueKind.Number
            ? d.GetInt32().ToString() : "(全部門)";

        Console.WriteLine($"    利用者 : {b.GetProperty("username").GetString()}");
        Console.WriteLine($"    uid    : {b.GetProperty("uid").GetString()}");
        Console.WriteLine($"    ロール : {roles}");
        Console.WriteLine($"    部門   : {dept}");
    }

    public async Task ShowEmployeesAsync()
    {
        var body = await GetJsonAsync("/api/employees");
        if (body is null) return;
        var b = body.Value;

        Console.WriteLine($"    {b.GetProperty("message").GetString()}");
        Console.WriteLine($"    取得件数: {b.GetProperty("count").GetInt32()} 件");
        Console.WriteLine();

        var employees = b.GetProperty("employees");
        var hasSal = employees.GetArrayLength() > 0 &&
                     employees[0].TryGetProperty("sal", out _);

        // 見出し（給与欄の有無で変わる）
        Console.WriteLine(hasSal
            ? "      EMPNO ENAME      JOB         DEPTNO        SAL       COMM"
            : "      EMPNO ENAME      JOB         DEPTNO");
        Console.WriteLine(hasSal
            ? "      ----- ---------- ---------- ------- ---------- ----------"
            : "      ----- ---------- ---------- -------");

        foreach (var e in employees.EnumerateArray())
        {
            var empno  = e.GetProperty("empno").GetInt32();
            var ename  = e.GetProperty("ename").GetString();
            var job    = e.GetProperty("job").GetString();
            var deptno = e.GetProperty("deptno").GetInt32();

            if (hasSal)
            {
                var sal  = e.TryGetProperty("sal", out var s) && s.ValueKind == JsonValueKind.Number
                            ? s.GetDecimal().ToString("N0") : "";
                var comm = e.TryGetProperty("comm", out var c) && c.ValueKind == JsonValueKind.Number
                            ? c.GetDecimal().ToString("N0") : "";
                Console.WriteLine($"      {empno,5} {ename,-10} {job,-10} {deptno,7} {sal,10} {comm,10}");
            }
            else
            {
                Console.WriteLine($"      {empno,5} {ename,-10} {job,-10} {deptno,7}");
            }
        }

        if (!hasSal)
            Console.WriteLine("      ※ 給与(SAL)は権限が無いため、そもそもサーバから返されていません。");
    }

    public async Task ShowSummaryAsync()
    {
        var body = await GetJsonAsync("/api/employees/summary");
        if (body is null) return;
        var b = body.Value;

        Console.WriteLine($"    {b.GetProperty("message").GetString()}");
        Console.WriteLine("      DEPTNO   人数   給与合計");
        Console.WriteLine("      ------ ------ ----------");
        foreach (var s in b.GetProperty("summary").EnumerateArray())
        {
            var deptno = s.GetProperty("deptno").GetInt32();
            var count  = s.GetProperty("count").GetInt32();
            var total  = s.TryGetProperty("total_sal", out var t) && t.ValueKind == JsonValueKind.Number
                          ? t.GetDecimal().ToString("N0") : "";
            Console.WriteLine($"      {deptno,6} {count,6} {total,10}");
        }
    }
}

record TokenResponse(string access_token, string token_type, int expires_in, string? refresh_token);
