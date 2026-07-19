using System.Collections.Concurrent;
using System.DirectoryServices.Protocols;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Oracle.ManagedDataAccess.Client;

// =============================================================================
//  JWT 認証デモ サーバ（ステップ7：業務データとの連携／仕上げ）
//
//  ステップ6からの変更点：
//    ・検証済みの JWT に載っている「利用者」と「ロール」を使って、
//      Oracle 19c の EMP テーブルから業務データを取得する。
//    ・ロールに応じて、見える行と見える列を変える。
//        管理者(plm-admins)   … 全部門の全行 ＋ 給与(SAL)・歩合(COMM) も見える
//        一般利用者(plm-users) … 自部門の行のみ ＋ 給与は見えない
//
//  ★これで一連の流れがつながる：
//      SSO で認証 → 引換券 → JWT（識別子＋ロール）→ 権限に応じた業務データ
//
//  ★対応づけ（デモの割り切り）
//      01PLM01 → DEPTNO 20 ／ 01PLM02 → DEPTNO 30
//      本番では、この対応は PLM のマスタ（DB）が持つべきもの。README 参照。
// =============================================================================

Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// JWT の設定値
// -----------------------------------------------------------------------------
const string SigningKey = "this-is-a-demo-secret-key-please-change-32bytes!";
const string Issuer     = "JwtDemoServer";
const string Audience   = "JwtDemoClient";
const string RoleClaim  = "role";

const int AccessTokenMinutes = 5;
const int TicketSeconds      = 60;
const int RefreshTokenHours  = 8;

// -----------------------------------------------------------------------------
// SSO 連携の設定（ステップ6と同じ。★本番でヘッダーを信用しないこと）
// -----------------------------------------------------------------------------
const string RemoteUserHeader   = "X-Remote-User";
const bool   LoopbackOnlyForSso = true;

// -----------------------------------------------------------------------------
// ApacheDS(LDAP) 接続設定
// -----------------------------------------------------------------------------
const string LdapHost      = "localhost";
const int    LdapPort      = 10389;
const string LdapBindDn    = "uid=idp-reader,ou=people,dc=example,dc=com";
const string LdapBindPass  = "idp-reader";
const string LdapBaseDn    = "ou=people,dc=example,dc=com";
const string LdapLoginAttr = "uid";
const string LdapMailAttr  = "mail";

const string LdapGroupBaseDn    = "ou=groups,dc=example,dc=com";
const string LdapGroupMemberAtt = "member";
const string LdapGroupNameAtt   = "cn";

// -----------------------------------------------------------------------------
// ★ステップ7で追加：Oracle 接続設定
//    デモのため scott/tiger を直書き。本番は設定/シークレットストアへ。
// -----------------------------------------------------------------------------
const string OracleConnStr = "User Id=scott;Password=tiger;Data Source=localhost:1521/xe";

// ★利用者と部門の対応づけ（デモの割り切り。本番は PLM のマスタが持つ）
var userDepartment = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
{
    ["01PLM01"] = 20,
    ["01PLM02"] = 30,
};

var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));

var tickets  = new ConcurrentDictionary<string, StoreEntry>();
var refreshs = new ConcurrentDictionary<string, StoreEntry>();

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
            RoleClaimType    = RoleClaim,
            NameClaimType    = JwtRegisteredClaimNames.Sub
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("plm-admins"));
    options.AddPolicy("PlmUser",   p => p.RequireRole("plm-users", "plm-admins"));
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

Console.WriteLine($"[LDAP  ] ldap://{LdapHost}:{LdapPort}");
Console.WriteLine($"[SSO   ] 引換券発行: ヘッダー {RemoteUserHeader} / ループバック限定={LoopbackOnlyForSso}");
Console.WriteLine($"[ORACLE] {OracleConnStr.Replace("Password=tiger", "Password=***")}");

// =============================================================================
//  【SSO 経路】引換券の発行・交換／リフレッシュ（ステップ6と同じ）
// =============================================================================
app.MapGet("/sso/ticket", (HttpContext ctx) =>
{
    if (LoopbackOnlyForSso)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null || !IPAddress.IsLoopback(ip))
        {
            Console.WriteLine($"[SSO   ] 拒否: ループバック以外からの要求 ({ip})");
            return Results.StatusCode(403);
        }
    }

    var remoteUser = ctx.Request.Headers[RemoteUserHeader].ToString();
    if (string.IsNullOrWhiteSpace(remoteUser))
    {
        Console.WriteLine("[SSO   ] 拒否: REMOTE_USER が無い（SSO 未認証）");
        return Results.Unauthorized();
    }

    LdapUser? user;
    try { user = LdapLookup(remoteUser); }
    catch (Exception ex)
    {
        Console.WriteLine($"[LDAP  ] エラー: {ex.Message}");
        return Results.Problem($"LDAP 検索に失敗しました: {ex.Message}", statusCode: 503);
    }
    if (user is null)
    {
        Console.WriteLine($"[SSO   ] 拒否: {remoteUser} はディレクトリに存在しない");
        return Results.Unauthorized();
    }

    var ticket = NewSecret();
    tickets[ticket] = new StoreEntry(user.Uid, DateTime.UtcNow.AddSeconds(TicketSeconds));
    Console.WriteLine($"[SSO   ] 引換券を発行: {user.Uid}（{TicketSeconds}秒・一度きり）");

    return Results.Ok(new { ticket, expires_in = TicketSeconds, user = user.Subject });
});

app.MapPost("/token/exchange", (ExchangeRequest req) =>
{
    if (req.Ticket is null || !tickets.TryRemove(req.Ticket, out var entry))
        return Results.Unauthorized();
    if (DateTime.UtcNow > entry.ExpiresUtc)
        return Results.Unauthorized();

    return IssueTokensFor(entry.Uid, "引換券");
});

app.MapPost("/token/refresh", (RefreshRequest req) =>
{
    if (req.RefreshToken is null || !refreshs.TryRemove(req.RefreshToken, out var entry))
        return Results.Unauthorized();
    if (DateTime.UtcNow > entry.ExpiresUtc)
        return Results.Unauthorized();

    return IssueTokensFor(entry.Uid, "リフレッシュ");
});

app.MapPost("/token", (LoginRequest req) =>
{
    LdapUser? user;
    try { user = LdapAuthenticate(req.Username, req.Password); }
    catch (Exception ex)
    {
        return Results.Problem($"LDAP 接続または検索に失敗しました: {ex.Message}", statusCode: 503);
    }
    if (user is null) return Results.Unauthorized();

    return IssueTokensFor(user.Uid, "パスワード");
});

// -----------------------------------------------------------------------------
// GET /api/whoami … 認証のみ
// -----------------------------------------------------------------------------
app.MapGet("/api/whoami", (ClaimsPrincipal user) =>
{
    var info = Describe(user);
    var dept = info.uid is not null && userDepartment.TryGetValue(info.uid, out var d) ? (int?)d : null;

    return Results.Ok(new
    {
        message = "JWT の検証に成功しました。",
        info.username, info.uid, info.email, info.roles,
        department = dept   // どの部門のデータが見えるか
    });
}).RequireAuthorization();

// =============================================================================
//  ★ステップ7の中心：GET /api/employees
//    JWT のロールに応じて、見える「行」と「列」を変えて EMP から取得する。
// =============================================================================
app.MapGet("/api/employees", (ClaimsPrincipal user) =>
{
    var info    = Describe(user);
    var isAdmin = user.IsInRole("plm-admins");

    // --- 一般利用者は「自分の部門」しか見られない ---
    int? dept = null;
    if (!isAdmin)
    {
        if (info.uid is null || !userDepartment.TryGetValue(info.uid, out var d))
        {
            // 部門が特定できない利用者には業務データを見せない
            Console.WriteLine($"[ORACLE] {info.uid} の部門が未定義のため拒否");
            return Results.Forbid();
        }
        dept = d;
    }

    try
    {
        var rows = QueryEmployees(isAdmin, dept);
        Console.WriteLine($"[ORACLE] {info.uid} に {rows.Count} 件を返却" +
                          $"（{(isAdmin ? "全部門・給与あり" : $"DEPTNO={dept}・給与なし")}）");

        return Results.Ok(new
        {
            message = isAdmin
                ? "全部門の従業員一覧（管理者：給与を含む）"
                : $"所属部門（DEPTNO={dept}）の従業員一覧（給与は非表示）",
            user       = info.username,
            roles      = info.roles,
            department = dept,
            count      = rows.Count,
            employees  = rows
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ORACLE] エラー: {ex.Message}");
        return Results.Problem($"業務データの取得に失敗しました: {ex.Message}", statusCode: 503);
    }
});

// =============================================================================
//  GET /api/employees/summary … 管理者のみ。部門別の人数と給与合計。
// =============================================================================
app.MapGet("/api/employees/summary", (ClaimsPrincipal user) =>
{
    try
    {
        var rows = QuerySummary();
        return Results.Ok(new { message = "部門別サマリ（管理者のみ）", summary = rows });
    }
    catch (Exception ex)
    {
        return Results.Problem($"集計に失敗しました: {ex.Message}", statusCode: 503);
    }
}).RequireAuthorization("AdminOnly");

// -----------------------------------------------------------------------------
// 従来の部品 API（ステップ5・6から継続）
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
}).RequireAuthorization("PlmUser");

app.MapPost("/api/parts", (ClaimsPrincipal user) =>
{
    var info = Describe(user);
    return Results.Ok(new { message = "部品を更新しました（管理者操作）", user = info.username });
}).RequireAuthorization("AdminOnly");

app.MapGet("/", () => "JwtDemoServer (step7/Oracle) is running.");

app.Run();


// =============================================================================
//  ★Oracle：EMP から従業員を取得する。
//    ・管理者     … 全行、SAL/COMM 込み
//    ・一般利用者 … 指定部門のみ、SAL/COMM は返さない
//
//  ★重要：部門の絞り込みは「バインド変数」で渡す（文字列連結しない）。
//    SQL インジェクション対策であり、LDAP フィルタのエスケープと同じ発想。
// =============================================================================
List<Dictionary<string, object?>> QueryEmployees(bool isAdmin, int? deptno)
{
    var result = new List<Dictionary<string, object?>>();

    // 見せる列をロールで変える（給与は管理者だけ）
    var sql = isAdmin
        ? "SELECT EMPNO, ENAME, JOB, HIREDATE, SAL, COMM, DEPTNO FROM EMP ORDER BY EMPNO"
        : "SELECT EMPNO, ENAME, JOB, HIREDATE, DEPTNO FROM EMP WHERE DEPTNO = :deptno ORDER BY EMPNO";

    using var conn = new OracleConnection(OracleConnStr);
    conn.Open();

    using var cmd = new OracleCommand(sql, conn);
    cmd.BindByName = true;
    if (!isAdmin)
        cmd.Parameters.Add(new OracleParameter("deptno", deptno));

    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var row = new Dictionary<string, object?>
        {
            ["empno"]    = reader.GetInt32(reader.GetOrdinal("EMPNO")),
            ["ename"]    = reader.GetString(reader.GetOrdinal("ENAME")),
            ["job"]      = reader.GetString(reader.GetOrdinal("JOB")),
            ["hiredate"] = reader.GetDateTime(reader.GetOrdinal("HIREDATE")).ToString("yyyy-MM-dd"),
            ["deptno"]   = reader.GetInt32(reader.GetOrdinal("DEPTNO")),
        };

        if (isAdmin)
        {
            var iSal  = reader.GetOrdinal("SAL");
            var iComm = reader.GetOrdinal("COMM");
            row["sal"]  = reader.IsDBNull(iSal)  ? null : (decimal?)reader.GetDecimal(iSal);
            row["comm"] = reader.IsDBNull(iComm) ? null : (decimal?)reader.GetDecimal(iComm);
        }

        result.Add(row);
    }
    return result;
}

// =============================================================================
//  ★Oracle：部門別のサマリ（管理者のみが呼べる API から使う）
// =============================================================================
List<Dictionary<string, object?>> QuerySummary()
{
    var result = new List<Dictionary<string, object?>>();
    const string sql = @"SELECT DEPTNO, COUNT(*) AS CNT, SUM(SAL) AS TOTAL_SAL
                         FROM EMP GROUP BY DEPTNO ORDER BY DEPTNO";

    using var conn = new OracleConnection(OracleConnStr);
    conn.Open();
    using var cmd = new OracleCommand(sql, conn);
    using var reader = cmd.ExecuteReader();

    while (reader.Read())
    {
        result.Add(new Dictionary<string, object?>
        {
            ["deptno"]    = reader.GetInt32(0),
            ["count"]     = reader.GetInt32(1),
            ["total_sal"] = reader.IsDBNull(2) ? null : (decimal?)reader.GetDecimal(2),
        });
    }
    return result;
}

// =============================================================================
//  トークン発行（ステップ6と同じ）
// =============================================================================
IResult IssueTokensFor(string uid, string via)
{
    LdapUser? user;
    string[] roles;
    try
    {
        user = LdapLookup(uid);
        if (user is null) return Results.Unauthorized();
        roles = LdapFindRoles(user.Dn);
    }
    catch (Exception ex)
    {
        return Results.Problem($"属性の解決に失敗しました: {ex.Message}", statusCode: 503);
    }

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Subject),
        new("uid", user.Uid),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new("purpose", "smartclient-demo"),
        new("auth_via", via),
    };
    if (user.Mail is not null)
        claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Mail));
    foreach (var r in roles)
        claims.Add(new Claim(RoleClaim, r));

    var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(Issuer, Audience, claims,
        DateTime.UtcNow, DateTime.UtcNow.AddMinutes(AccessTokenMinutes), creds);
    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    var refresh = NewSecret();
    refreshs[refresh] = new StoreEntry(user.Uid, DateTime.UtcNow.AddHours(RefreshTokenHours));

    Console.WriteLine($"[JWT   ] {user.Uid} にトークンを発行（経路: {via}／ロール: " +
                      $"{(roles.Length == 0 ? "なし" : string.Join(", ", roles))}）");

    return Results.Ok(new
    {
        access_token  = jwt,
        token_type    = "Bearer",
        expires_in    = AccessTokenMinutes * 60,
        refresh_token = refresh
    });
}

static string NewSecret() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

static (string username, string? uid, string? email, string[] roles) Describe(ClaimsPrincipal user)
{
    var username =
        user.FindFirstValue("sub")
        ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.Identity?.Name
        ?? "(unknown)";

    var email = user.FindFirstValue(JwtRegisteredClaimNames.Email)
                ?? user.FindFirstValue(ClaimTypes.Email);

    var roles = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role))
                    .Select(c => c.Value).Distinct().ToArray();

    return (username, user.FindFirstValue("uid"), email, roles);
}

// =============================================================================
//  LDAP 関連（ステップ6と同じ）
// =============================================================================
LdapUser? LdapLookup(string identifier)
{
    if (string.IsNullOrWhiteSpace(identifier)) return null;

    var safe = EscapeLdapFilter(identifier);
    var id = new LdapDirectoryIdentifier(LdapHost, LdapPort);

    using var conn = new LdapConnection(id) { AuthType = AuthType.Basic };
    conn.SessionOptions.ProtocolVersion = 3;
    conn.Credential = new NetworkCredential(LdapBindDn, LdapBindPass);
    conn.Bind();

    var filter = $"(|({LdapLoginAttr}={safe})({LdapMailAttr}={safe}))";
    var request = new SearchRequest(LdapBaseDn, filter, SearchScope.Subtree, LdapLoginAttr, LdapMailAttr);
    var response = (SearchResponse)conn.SendRequest(request);

    if (response.Entries.Count != 1) return null;

    var entry = response.Entries[0];
    var uid  = entry.Attributes.Contains(LdapLoginAttr)
                 ? entry.Attributes[LdapLoginAttr][0]?.ToString() ?? identifier : identifier;
    var mail = entry.Attributes.Contains(LdapMailAttr)
                 ? entry.Attributes[LdapMailAttr][0]?.ToString() : null;

    return new LdapUser(uid, mail, entry.DistinguishedName);
}

LdapUser? LdapAuthenticate(string username, string password)
{
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return null;

    var user = LdapLookup(username);
    if (user is null) return null;

    var id = new LdapDirectoryIdentifier(LdapHost, LdapPort);
    using var verifyConn = new LdapConnection(id) { AuthType = AuthType.Basic };
    verifyConn.SessionOptions.ProtocolVersion = 3;
    verifyConn.Credential = new NetworkCredential(user.Dn, password);
    try { verifyConn.Bind(); }
    catch (LdapException) { return null; }
    return user;
}

string[] LdapFindRoles(string userDn)
{
    var id = new LdapDirectoryIdentifier(LdapHost, LdapPort);
    using var conn = new LdapConnection(id) { AuthType = AuthType.Basic };
    conn.SessionOptions.ProtocolVersion = 3;
    conn.Credential = new NetworkCredential(LdapBindDn, LdapBindPass);
    conn.Bind();

    var filter  = $"(&(objectClass=groupOfNames)({LdapGroupMemberAtt}={EscapeLdapFilter(userDn)}))";
    var request = new SearchRequest(LdapGroupBaseDn, filter, SearchScope.Subtree, LdapGroupNameAtt);

    SearchResponse response;
    try { response = (SearchResponse)conn.SendRequest(request); }
    catch (DirectoryOperationException) { return Array.Empty<string>(); }

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

record LdapUser(string Uid, string? Mail, string Dn)
{
    public string Subject => Mail ?? Uid;
}

record StoreEntry(string Uid, DateTime ExpiresUtc);
record LoginRequest(string Username, string Password);
record ExchangeRequest(string? Ticket);
record RefreshRequest(string? RefreshToken);
