using System.DirectoryServices.Protocols;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// =============================================================================
//  JWT 認証デモ サーバ（ステップ4：初回認証を ApacheDS(LDAP) に接続）
//
//  ステップ1からの変更点：
//    ・POST /token の「初回認証」を、固定ユーザー辞書から
//      ApacheDS への LDAP 認証（検索 → 本人バインド）に差し替え。
//    ・LDAP から取得した mail を JWT の sub に採用（SSO 側 REMOTE_USER と揃える）。
//      併せて uid / email も別クレームとして持たせる。
//
//  JWT の発行・検証のしくみ自体はステップ1のまま（HS256・5分・検証パラメータ）。
//  クライアント（JwtDemoClient）は変更なし。
// =============================================================================

//JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// JWT の設定値（デモ用にコードに直書き。実運用では設定ファイル/シークレットに置く）
// -----------------------------------------------------------------------------
const string SigningKey = "this-is-a-demo-secret-key-please-change-32bytes!";
const string Issuer     = "JwtDemoServer";
const string Audience   = "JwtDemoClient";

// -----------------------------------------------------------------------------
// ★ ApacheDS(LDAP) 接続設定 ― 実機の構成に合わせて必ず確認・変更する
//    （Shibboleth Windows 版手順書で構築した ApacheDS の値に合わせる）
// -----------------------------------------------------------------------------
#if true
const string LdapHost      = "localhost";
const int    LdapPort      = 10389;
const string LdapBindDn    = "uid=idp-reader,ou=people,dc=example,dc=com";
const string LdapBindPass  = "idp-reader";
const string LdapBaseDn    = "ou=people,dc=example,dc=com";
const string LdapLoginAttr = "uid";
const string LdapMailAttr  = "mail";
#else
const string LdapHost      = "localhost";
const int    LdapPort      = 10389;                  // ApacheDS 既定の平文ポート
const string LdapBindDn    = "uid=admin,ou=system";  // 検索用にバインドするアカウント（デモは管理者）
const string LdapBindPass  = "secret";               // ApacheDS 既定の admin パスワード
const string LdapBaseDn    = "dc=example,dc=com";    // 利用者を探す起点（既定パーティション）
const string LdapLoginAttr = "uid";                  // ログイン名が入っている属性
const string LdapMailAttr  = "mail";                 // 識別子に使うメール属性
#endif
var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));

// -----------------------------------------------------------------------------
// 認証（JWT Bearer）の登録：受け取った JWT をどう検証するか（ステップ1のまま）
// -----------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey,
            ValidateIssuer   = true,
            ValidIssuer      = Issuer,
            ValidateAudience = true,
            ValidAudience    = Audience,
            ValidateLifetime = true,
            ClockSkew        = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

Console.WriteLine($"[LDAP] 認証先: ldap://{LdapHost}:{LdapPort}  base={LdapBaseDn}  loginAttr={LdapLoginAttr}");

// -----------------------------------------------------------------------------
// POST /token … 初回認証（LDAP）してトークンを発行する
// -----------------------------------------------------------------------------
app.MapPost("/token", (LoginRequest req) =>
{
    // --- LDAP で本人確認 ---
    (string uid, string? mail)? auth;
    try
    {
        auth = LdapAuthenticate(req.Username, req.Password);
    }
    catch (Exception ex)
    {
        // 認証失敗ではなく「LDAP に繋がらない/検索できない」などのサーバ側事情。
        // 401（資格情報が違う）とは区別して返す。
        Console.WriteLine($"[LDAP] エラー: {ex.Message}");
        return Results.Problem($"LDAP 接続または検索に失敗しました: {ex.Message}", statusCode: 503);
    }

    if (auth is null)
    {
        // ユーザーが見つからない or パスワード不一致 → トークンは発行しない
        return Results.Unauthorized();
    }

    var (uid, mail) = auth.Value;

    // --- 識別子の統一 ---
    // sub は SSO(.aspx) 側の REMOTE_USER と揃える形（メール形式）を第一候補にする。
    // メールが無ければ uid で代用。アプリが使いやすいよう uid も別クレームで持たせる。
    var subject = mail ?? uid;

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, subject),
        new("uid", uid),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new("purpose", "smartclient-demo"),
    };
    if (mail is not null)
        claims.Add(new Claim(JwtRegisteredClaimNames.Email, mail));

    var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer:             Issuer,
        audience:           Audience,
        claims:             claims,
        notBefore:          DateTime.UtcNow,
        expires:            DateTime.UtcNow.AddMinutes(5),
        signingCredentials: creds
    );
    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new
    {
        access_token = jwt,
        token_type   = "Bearer",
        expires_in   = 300
    });
});

// -----------------------------------------------------------------------------
// GET /api/whoami … 要・認証。JWT を検証し、トークン内の識別子を返す（ステップ1の修正版）
// -----------------------------------------------------------------------------
app.MapGet("/api/whoami", (ClaimsPrincipal user) =>
{
    // sub は環境により "sub" のまま/URI 形式に変換される場合があるので複数候補で拾う
    var username =
        user.FindFirstValue("sub")
        ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.Identity?.Name
        ?? "(unknown)";

    var uid     = user.FindFirstValue("uid");
#if true
	// 応答のclaims配列を見ると、subに入れた値が
	// http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier
	// という URI 形式になっている。
	// .NETのクレーム名変換は起こる前提で、取り出しは常に多候補で行う
	var email = user.FindFirstValue(JwtRegisteredClaimNames.Email)
	    ?? user.FindFirstValue(ClaimTypes.Email);
#else
    var email   = user.FindFirstValue(JwtRegisteredClaimNames.Email);
#endif
	var purpose = user.FindFirstValue("purpose");

    // 参考：実際にトークンへ入っている全クレーム（デバッグ用）
    var allClaims = user.Claims.Select(c => new { c.Type, c.Value }).ToArray();

    return Results.Ok(new
    {
        message  = "JWT の検証に成功しました。",
        username = username,   // ＝ sub（SSO の REMOTE_USER 相当）
        uid      = uid,        // ＝ 正規化した PLM の識別番号
        email    = email,
        purpose  = purpose,
        claims   = allClaims
    });
})
.RequireAuthorization();

// 動作確認用（認証不要）
app.MapGet("/", () => "JwtDemoServer (step4/LDAP) is running. POST /token then GET /api/whoami with Bearer token.");

app.Run();


// =============================================================================
//  LDAP 認証：検索用アカウントで利用者を探し、見つけた DN＋入力パスワードで
//  もう一度バインドしてパスワードを検証する（＝ 検索 → 本人バインド 方式）。
//  Shibboleth IdP が LDAP に対して行う認証と同じ考え方。
//  成功なら (uid, mail) を返し、失敗（未存在/パスワード不一致）なら null。
//  接続不可などは例外として呼び出し側へ投げる。
// =============================================================================
(string uid, string? mail)? LdapAuthenticate(string username, string password)
{
    // 空パスワードでの simple bind は「未認証バインド」と解釈され、
    // サーバによっては検証を素通りして成功扱いになる。明示的に弾く。
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        return null;

    var safeName = EscapeLdapFilter(username);
    var id = new LdapDirectoryIdentifier(LdapHost, LdapPort);

    // (1) 検索用アカウントでバインドし、利用者の DN と属性を取得
    string userDn;
    string  uid;
    string? mail;
    using (var searchConn = new LdapConnection(id) { AuthType = AuthType.Basic })
    {
        searchConn.SessionOptions.ProtocolVersion = 3;
        searchConn.Credential = new NetworkCredential(LdapBindDn, LdapBindPass);
        searchConn.Bind();   // ここで失敗＝検索アカウントの設定ミス（例外→503）

        var filter  = $"({LdapLoginAttr}={safeName})";
        var request = new SearchRequest(LdapBaseDn, filter, SearchScope.Subtree,
                                        LdapLoginAttr, LdapMailAttr);
        var response = (SearchResponse)searchConn.SendRequest(request);

        if (response.Entries.Count != 1)
            return null;     // 見つからない、または複数該当（あいまい）

        var entry = response.Entries[0];
        userDn = entry.DistinguishedName;
        uid    = entry.Attributes.Contains(LdapLoginAttr)
                    ? entry.Attributes[LdapLoginAttr][0]?.ToString() ?? username
                    : username;
        mail   = entry.Attributes.Contains(LdapMailAttr)
                    ? entry.Attributes[LdapMailAttr][0]?.ToString()
                    : null;
    }

    // (2) 見つけた DN＋入力パスワードでバインド → パスワード検証
    using (var verifyConn = new LdapConnection(id) { AuthType = AuthType.Basic })
    {
        verifyConn.SessionOptions.ProtocolVersion = 3;
        verifyConn.Credential = new NetworkCredential(userDn, password);
        try
        {
            verifyConn.Bind();   // 成功＝パスワード一致
        }
        catch (LdapException)
        {
            return null;         // パスワード不一致（invalid credentials）
        }
    }

    return (uid, mail);
}

// LDAP 検索フィルタ用のエスケープ（LDAP インジェクション対策・RFC4515）
static string EscapeLdapFilter(string s)
{
    var sb = new StringBuilder(s.Length);
    foreach (var c in s)
    {
        switch (c)
        {
            case '\\': sb.Append("\\5c"); break;
            case '*':  sb.Append("\\2a"); break;
            case '(':  sb.Append("\\28"); break;
            case ')':  sb.Append("\\29"); break;
            case '\0': sb.Append("\\00"); break;
            default:   sb.Append(c);      break;
        }
    }
    return sb.ToString();
}

// リクエストボディの型（POST /token で受け取る JSON に対応）
record LoginRequest(string Username, string Password);
