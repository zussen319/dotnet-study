using System.DirectoryServices.Protocols;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// =============================================================================
//  JWT 認証デモ サーバ（ステップ5：ロール／属性による認可）
//
//  ステップ4からの変更点：
//    ・LDAP のグループ（ou=groups 配下の groupOfNames）から利用者のロールを取得し、
//      JWT に role クレームとして載せる。
//    ・ロールに応じてアクセスを分岐する API を追加（認証 authn と 認可 authz の分離）。
//    ・.NET のクレーム名変換対策を JsonWebTokenHandler 側にも適用（ステップ4の知見）。
//
//  ★ここでの重要な区別：
//     401 Unauthorized … 誰だか分からない（トークンが無い/無効）＝認証の問題
//     403 Forbidden    … 誰かは分かるが権限が足りない        ＝認可の問題
// =============================================================================

// JWT の標準クレーム名を .NET の長い URI 名に変換させない。
// ★ステップ4の知見：.NET 8 以降の JwtBearer は既定で JsonWebTokenHandler を使うため、
//   JwtSecurityTokenHandler 側だけを消しても効かない。両方に対して行う。
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// JWT の設定値（デモ用にコードに直書き。実運用では設定ファイル/シークレットに置く）
// -----------------------------------------------------------------------------
const string SigningKey = "this-is-a-demo-secret-key-please-change-32bytes!";
const string Issuer     = "JwtDemoServer";
const string Audience   = "JwtDemoClient";

// ロールを入れるクレーム名。JWT の慣例に合わせて短い "role" を使う。
const string RoleClaim  = "role";

// -----------------------------------------------------------------------------
// ApacheDS(LDAP) 接続設定（ステップ4で実機確認済みの値）
// -----------------------------------------------------------------------------
const string LdapHost      = "localhost";
const int    LdapPort      = 10389;
const string LdapBindDn    = "uid=idp-reader,ou=people,dc=example,dc=com";
const string LdapBindPass  = "idp-reader";
const string LdapBaseDn    = "ou=people,dc=example,dc=com";
const string LdapLoginAttr = "uid";
const string LdapMailAttr  = "mail";

// ★ステップ5で追加：グループ（ロール）の検索設定
const string LdapGroupBaseDn    = "ou=groups,dc=example,dc=com";  // グループの入れ物
const string LdapGroupMemberAtt = "member";                       // groupOfNames は member に利用者 DN を持つ
const string LdapGroupNameAtt   = "cn";                           // ロール名として使う属性

var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));

// -----------------------------------------------------------------------------
// 認証（JWT Bearer）の登録
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
            ClockSkew        = TimeSpan.Zero,

            // ★どのクレームを「ロール」とみなすかを明示する。
            //   これを指定しないと RequireRole が既定の URI 形式クレームを探してしまう。
            RoleClaimType = RoleClaim,
            NameClaimType = JwtRegisteredClaimNames.Sub
        };
    });

// -----------------------------------------------------------------------------
// 認可ポリシー：ロール名と「業務上の権限」を切り離して定義する
//   → LDAP のグループ名が変わっても、ポリシー名（AdminOnly 等）は変えずに済む。
// -----------------------------------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("plm-admins"));
    options.AddPolicy("PlmUser",   p => p.RequireRole("plm-users", "plm-admins"));
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

Console.WriteLine($"[LDAP] 認証先: ldap://{LdapHost}:{LdapPort}  user={LdapBaseDn}  group={LdapGroupBaseDn}");

// -----------------------------------------------------------------------------
// POST /token … 初回認証（LDAP）＋ロール取得 → トークン発行
// -----------------------------------------------------------------------------
app.MapPost("/token", (LoginRequest req) =>
{
    (string uid, string? mail, string userDn)? auth;
    try
    {
        auth = LdapAuthenticate(req.Username, req.Password);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[LDAP] エラー: {ex.Message}");
        return Results.Problem($"LDAP 接続または検索に失敗しました: {ex.Message}", statusCode: 503);
    }

    if (auth is null)
        return Results.Unauthorized();

    var (uid, mail, userDn) = auth.Value;

    // ★ロール（グループ所属）を LDAP から取得する
    string[] roles;
    try
    {
        roles = LdapFindRoles(userDn);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[LDAP] ロール取得エラー: {ex.Message}");
        return Results.Problem($"ロールの取得に失敗しました: {ex.Message}", statusCode: 503);
    }
    Console.WriteLine($"[LDAP] {uid} のロール: {(roles.Length == 0 ? "(なし)" : string.Join(", ", roles))}");

    // --- JWT の組み立て ---
    var subject = mail ?? uid;   // 識別子の統一（ステップ4で確定：メール形式）

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, subject),
        new("uid", uid),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new("purpose", "smartclient-demo"),
    };
    if (mail is not null)
        claims.Add(new Claim(JwtRegisteredClaimNames.Email, mail));

    // ロールは複数入りうるので、同じ名前のクレームを複数追加する（JWT では配列になる）
    foreach (var r in roles)
        claims.Add(new Claim(RoleClaim, r));

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
// GET /api/whoami … 認証だけ必要（ロールは問わない）
// -----------------------------------------------------------------------------
app.MapGet("/api/whoami", (ClaimsPrincipal user) =>
{
    var info = Describe(user);
    return Results.Ok(new
    {
        message = "JWT の検証に成功しました。",
        info.username, info.uid, info.email, info.roles,
        claims = user.Claims.Select(c => new { c.Type, c.Value }).ToArray()
    });
})
.RequireAuthorization();

// -----------------------------------------------------------------------------
// GET /api/parts … 一般利用者以上が閲覧できる業務データ（PLM の部品一覧を想定）
//   plm-users または plm-admins のどちらかがあれば OK。
// -----------------------------------------------------------------------------
app.MapGet("/api/parts", (ClaimsPrincipal user) =>
{
    var info = Describe(user);
    return Results.Ok(new
    {
        message = "部品一覧（閲覧）",
        user    = info.username,
        parts   = new[]
        {
            new { code = "P-1001", name = "ブラケット", rev = "A" },
            new { code = "P-1002", name = "シャフト",   rev = "B" },
        }
    });
})
.RequireAuthorization("PlmUser");

// -----------------------------------------------------------------------------
// POST /api/parts … 部品の更新（管理者ロールのみ）
//   一般利用者のトークンで呼ぶと 403（認証は通るが権限が足りない）。
// -----------------------------------------------------------------------------
app.MapPost("/api/parts", (ClaimsPrincipal user) =>
{
    var info = Describe(user);
    return Results.Ok(new
    {
        message = "部品を更新しました（管理者操作）",
        user    = info.username
    });
})
.RequireAuthorization("AdminOnly");

// -----------------------------------------------------------------------------
// GET /api/report … 「コード内で分岐」する例。
//   ポリシーで弾かずに、ロールに応じて返す内容を変える（PLM でよくある形）。
// -----------------------------------------------------------------------------
app.MapGet("/api/report", (ClaimsPrincipal user) =>
{
    var info = Describe(user);
    var isAdmin = user.IsInRole("plm-admins");   // ★ロールによる分岐

    return Results.Ok(new
    {
        message = isAdmin ? "管理者向けレポート（原価情報を含む）" : "一般向けレポート（原価情報なし）",
        user    = info.username,
        roles   = info.roles,
        cost    = isAdmin ? "1,250 円/個" : null   // 管理者だけに見せる項目
    });
})
.RequireAuthorization();

// 動作確認用（認証不要）
app.MapGet("/", () => "JwtDemoServer (step5/roles) is running.");

app.Run();


// =============================================================================
//  ClaimsPrincipal から表示用の情報を取り出す。
//  ★クレーム名の変換に備え、いずれも「多候補」で拾う（ステップ4の知見）。
// =============================================================================
static (string username, string? uid, string? email, string[] roles) Describe(ClaimsPrincipal user)
{
    var username =
        user.FindFirstValue("sub")
        ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.Identity?.Name
        ?? "(unknown)";

    var email =
        user.FindFirstValue(JwtRegisteredClaimNames.Email)   // "email"
        ?? user.FindFirstValue(ClaimTypes.Email);            // URI 形式に変換された場合

    var uid = user.FindFirstValue("uid");

    var roles = user.FindAll("role")
                    .Concat(user.FindAll(ClaimTypes.Role))
                    .Select(c => c.Value)
                    .Distinct()
                    .ToArray();

    return (username, uid, email, roles);
}

// =============================================================================
//  LDAP 認証：検索 → 本人バインド（ステップ4と同じ。戻り値に userDn を追加）
// =============================================================================
(string uid, string? mail, string userDn)? LdapAuthenticate(string username, string password)
{
    // 空パスワードでの simple bind は「未認証バインド」と解釈されうるので明示的に弾く
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        return null;

    var safeName = EscapeLdapFilter(username);
    var id = new LdapDirectoryIdentifier(LdapHost, LdapPort);

    string userDn;
    string  uid;
    string? mail;

    // (1) 検索用アカウントで利用者を検索し、DN と属性を得る
    using (var searchConn = new LdapConnection(id) { AuthType = AuthType.Basic })
    {
        searchConn.SessionOptions.ProtocolVersion = 3;
        searchConn.Credential = new NetworkCredential(LdapBindDn, LdapBindPass);
        searchConn.Bind();

        var filter  = $"({LdapLoginAttr}={safeName})";
        var request = new SearchRequest(LdapBaseDn, filter, SearchScope.Subtree,
                                        LdapLoginAttr, LdapMailAttr);
        var response = (SearchResponse)searchConn.SendRequest(request);

        if (response.Entries.Count != 1)
            return null;

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
            verifyConn.Bind();
        }
        catch (LdapException)
        {
            return null;   // パスワード不一致
        }
    }

    return (uid, mail, userDn);
}

// =============================================================================
//  ★ステップ5で追加：利用者 DN を member に持つグループを検索し、cn をロール名として返す。
//    Shibboleth IdP が属性を解決して SP に渡すのと同じ位置づけの処理。
// =============================================================================
string[] LdapFindRoles(string userDn)
{
    var id = new LdapDirectoryIdentifier(LdapHost, LdapPort);

    using var conn = new LdapConnection(id) { AuthType = AuthType.Basic };
    conn.SessionOptions.ProtocolVersion = 3;
    conn.Credential = new NetworkCredential(LdapBindDn, LdapBindPass);
    conn.Bind();

    // (&(objectClass=groupOfNames)(member=uid=01PLM01,ou=people,dc=example,dc=com))
    var filter  = $"(&(objectClass=groupOfNames)({LdapGroupMemberAtt}={EscapeLdapFilter(userDn)}))";
    var request = new SearchRequest(LdapGroupBaseDn, filter, SearchScope.Subtree, LdapGroupNameAtt);

    SearchResponse response;
    try
    {
        response = (SearchResponse)conn.SendRequest(request);
    }
    catch (DirectoryOperationException)
    {
        // ou=groups が未作成の場合など。ロール無しとして扱う（認証自体は成功させる）。
        return Array.Empty<string>();
    }

    var roles = new List<string>();
    foreach (SearchResultEntry entry in response.Entries)
    {
        if (entry.Attributes.Contains(LdapGroupNameAtt))
        {
            var cn = entry.Attributes[LdapGroupNameAtt][0]?.ToString();
            if (!string.IsNullOrEmpty(cn)) roles.Add(cn);
        }
    }
    return roles.ToArray();
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
