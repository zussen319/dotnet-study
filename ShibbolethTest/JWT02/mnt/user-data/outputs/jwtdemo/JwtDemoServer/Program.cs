using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// =============================================================================
//  JWT 認証デモ サーバ（ステップ1：固定ユーザーで発行・検証）
//
//  エンドポイント：
//    POST /token       … ユーザー名/パスワードを検証し、正しければ JWT を発行
//    GET  /api/whoami  … リクエストの JWT を検証し、トークン内のユーザー名を返す
//
//  署名方式：HS256（共通鍵。1つの秘密鍵で署名も検証も行う。デモ向け）
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// JWT の設定値（デモ用にコードに直書き。実運用では設定ファイル/シークレットに置く）
// -----------------------------------------------------------------------------
// 署名用の秘密鍵。HS256 では最低 32 バイト（256bit）必要。※デモ用の固定値。
const string SigningKey = "this-is-a-demo-secret-key-please-change-32bytes!";
const string Issuer     = "JwtDemoServer";   // 発行者（このサーバ）
const string Audience   = "JwtDemoClient";   // 想定する利用者（クライアント）

var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));

// -----------------------------------------------------------------------------
// 認証（JWT Bearer）の登録：受け取った JWT をどう検証するかを定義する
// -----------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // 署名の検証（改ざんされていないか）
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey,

            // 発行者・利用者の検証
            ValidateIssuer   = true,
            ValidIssuer      = Issuer,
            ValidateAudience = true,
            ValidAudience    = Audience,

            // 有効期限の検証
            ValidateLifetime = true,
            ClockSkew        = TimeSpan.Zero  // 既定は5分の猶予。デモでは猶予なしで厳密に
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();  // JWT を読み取り、検証する
app.UseAuthorization();   // [Authorize] 相当のアクセス制御を効かせる

// -----------------------------------------------------------------------------
// POST /token … 初回認証してトークンを発行する
// -----------------------------------------------------------------------------
app.MapPost("/token", (LoginRequest req) =>
{
    // ★デモ用の固定ユーザー。後のステップで LDAP(ApacheDS) 認証に差し替える。
    //   ここが「初回認証」＝そもそも誰なのかを確認する部分。
    var users = new Dictionary<string, string>
    {
        ["01PLM01"] = "01PLM01",
        ["01PLM02"] = "01PLM02",
    };

    if (!users.TryGetValue(req.Username, out var pw) || pw != req.Password)
    {
        // 認証失敗 → トークンは発行しない
        return Results.Unauthorized();
    }

    // --- ここから JWT の組み立て ---

    // クレーム（トークンに入れる情報）。誰なのか等を入れる。
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, req.Username),           // subject＝ユーザー識別子
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // トークン固有ID
        new Claim("purpose", "smartclient-demo"),                       // 任意の独自クレーム
    };

    // 署名情報（秘密鍵 + HS256）
    var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

    // トークン本体（発行者・利用者・有効期限・クレーム・署名）
    var token = new JwtSecurityToken(
        issuer:             Issuer,
        audience:           Audience,
        claims:             claims,
        notBefore:          DateTime.UtcNow,
        expires:            DateTime.UtcNow.AddMinutes(5),  // ★有効期限5分（デモ用に短め）
        signingCredentials: creds
    );

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    // クライアントにトークンと有効期限を返す
    return Results.Ok(new
    {
        access_token = jwt,
        token_type   = "Bearer",
        expires_in   = 300  // 秒
    });
});

// -----------------------------------------------------------------------------
// GET /api/whoami … 要・認証。JWT を検証し、トークン内のユーザー名を返す
//   .RequireAuthorization() により、有効な JWT が無いと 401 が返る。
// -----------------------------------------------------------------------------
app.MapGet("/api/whoami", (ClaimsPrincipal user) =>
{
    // ここに来た時点で JWT の検証は済んでいる（署名・発行者・利用者・期限すべてOK）。
    //
    // ★注意（実機で判明）：.NET の JWT ハンドラは、標準クレーム "sub" を
    //   URI 形式 "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
    //   （＝ ClaimTypes.NameIdentifier）に変換することがある（.NET 10 で確認）。
    //   そのため "sub" だけを探すと見つからず (unknown) になる。
    //   下記のように複数の候補を順に探すと、変換の有無にかかわらず確実に拾える。
    var username =
        user.FindFirstValue("sub")                          // 変換されていない場合
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)   // URI に変換された場合
        ?? user.Identity?.Name
        ?? "(unknown)";

    var purpose = user.FindFirstValue("purpose");

    // 参考：実際にトークンに入っている全クレーム（型名と値）。デバッグ時に有用。
    var allClaims = user.Claims.Select(c => new { c.Type, c.Value }).ToArray();

    return Results.Ok(new
    {
        message  = "JWT の検証に成功しました。",
        username = username,
        purpose  = purpose,
        note     = "これは whoami.asp の JWT 版です（SmartClient 想定）。",
        claims   = allClaims
    });
})
.RequireAuthorization();

// 動作確認用（認証不要）：サーバが起きているかの確認に使う
app.MapGet("/", () => "JwtDemoServer is running. POST /token then GET /api/whoami with Bearer token.");

app.Run();

// リクエストボディの型（POST /token で受け取る JSON に対応）
record LoginRequest(string Username, string Password);
