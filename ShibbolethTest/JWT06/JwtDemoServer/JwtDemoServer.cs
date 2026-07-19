using System.Collections.Concurrent;
using System.DirectoryServices.Protocols;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

// =============================================================================
//  JWT 認証デモ サーバ（ステップ6：SSO → JWT の橋渡し）
//
//  ステップ5からの変更点：
//    ・SSO 認証済みの利用者に対し、パスワード再入力なしで JWT を渡す経路を追加。
//    ・そのために「一度きり・短命の引換券（チケット）」方式を実装。
//    ・SmartClient が長時間動くことを想定し、リフレッシュトークンを追加。
//
//  ★本番の流れ（想定）
//    (1) 利用者はブラウザでメイン画面(.aspx)を開く。IIS の Shibboleth SP が
//        認証を済ませており、REMOTE_USER が確定している。
//    (2) .aspx が「引換券」を発行してもらい、SmartClient の起動情報に埋める。
//    (3) SmartClient が起動し、引換券を JWT に交換する（/token/exchange）。
//    (4) 以降は JWT で API を呼ぶ。期限が切れたらリフレッシュで更新。
//
//  ★なぜ JWT を直接渡さず「引換券」を挟むのか
//    起動パラメータや URL は、ログ・履歴・プロセス一覧などに残りやすい。
//    引換券は「一度使ったら無効・数十秒で失効」なので、漏れても被害が限定される。
//    JWT 本体は POST の応答ボディで受け取るため、そうした場所に残らない。
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

const int AccessTokenMinutes  = 5;    // JWT の有効期限（短命）
const int TicketSeconds       = 60;   // 引換券の有効期限（さらに短命・一度きり）
const int RefreshTokenHours   = 8;    // リフレッシュトークン（業務時間を想定）

// -----------------------------------------------------------------------------
// ★ SSO 連携の設定
//    デモでは Shibboleth SP の代わりに HTTP ヘッダーで REMOTE_USER を受け取る。
//    ★★ 重要 ★★
//    ヘッダーは誰でも詐称できる。本番では絶対にこのままにしないこと。
//    本番では下記のいずれかで「SP が確定した値であること」を担保する：
//      (a) この発行エンドポイント自体を IIS 上に置き、Shibboleth SP で保護する
//          （SP がセットする REMOTE_USER はクライアントから詐称できない）
//      (b) 発行エンドポイントを外部公開せず、.aspx からのサーバ間通信のみ許可し、
//          共有シークレット等で呼び出し元を認証する
//    デモでは (b) の簡易版として「ループバックからの接続のみ許可」する。
// -----------------------------------------------------------------------------
const string RemoteUserHeader   = "X-Remote-User";
const bool   LoopbackOnlyForSso = true;   // ループバック以外からの発行要求を拒否

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

const string LdapGroupBaseDn    = "ou=groups,dc=example,dc=com";
const string LdapGroupMemberAtt = "member";
const string LdapGroupNameAtt   = "cn";

var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));

// -----------------------------------------------------------------------------
// 引換券とリフレッシュトークンの保管庫（デモのためメモリ上）
//   本番では、Web サーバを複数台にするなら共有ストア（DB/Redis 等）が必要。
// -----------------------------------------------------------------------------
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

Console.WriteLine($"[LDAP] ldap://{LdapHost}:{LdapPort}  user={LdapBaseDn}  group={LdapGroupBaseDn}");
Console.WriteLine($"[SSO ] 引換券発行: ヘッダー {RemoteUserHeader} / ループバック限定={LoopbackOnlyForSso}");

// =============================================================================
//  【SSO 経路 (1)】GET /sso/ticket
//    SSO 認証済みの利用者（REMOTE_USER）に対して引換券を発行する。
//    本番では .aspx が（あるいは SP 配下の同等ページが）この役割を担う。
// =============================================================================
app.MapGet("/sso/ticket", (HttpContext ctx) =>
{
    // --- 呼び出し元の制限（デモの安全策）---
    if (LoopbackOnlyForSso)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null || !IPAddress.IsLoopback(ip))
        {
            Console.WriteLine($"[SSO ] 拒否: ループバック以外からの要求 ({ip})");
            return Results.StatusCode(403);
        }
    }

    // --- SP が確定した利用者を受け取る（本番は Request.ServerVariables["REMOTE_USER"] 相当）---
    var remoteUser = ctx.Request.Headers[RemoteUserHeader].ToString();
    if (string.IsNullOrWhiteSpace(remoteUser))
    {
        // SSO 未認証。本番なら SP がここに来る前にログイン画面へ飛ばしている。
        Console.WriteLine("[SSO ] 拒否: REMOTE_USER が無い（SSO 未認証）");
        return Results.Unauthorized();
    }

    // --- 実在確認（存在しない利用者に引換券を出さない）---
    LdapUser? user;
    try
    {
        user = LdapLookup(remoteUser);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[LDAP] エラー: {ex.Message}");
        return Results.Problem($"LDAP 検索に失敗しました: {ex.Message}", statusCode: 503);
    }
    if (user is null)
    {
        Console.WriteLine($"[SSO ] 拒否: {remoteUser} はディレクトリに存在しない");
        return Results.Unauthorized();
    }

    // --- 引換券を発行（推測不能な乱数・短命・一度きり）---
    var ticket = NewSecret();
    tickets[ticket] = new StoreEntry(user.Uid, DateTime.UtcNow.AddSeconds(TicketSeconds));
    Console.WriteLine($"[SSO ] 引換券を発行: {user.Uid}（{TicketSeconds}秒・一度きり）");

    return Results.Ok(new
    {
        ticket,
        expires_in = TicketSeconds,
        user       = user.Subject,
        // 本番では、この値を SmartClient の起動パラメータに埋め込む
        note       = "SmartClient を起動し、この ticket を /token/exchange で JWT に交換する"
    });
});

// =============================================================================
//  【SSO 経路 (2)】POST /token/exchange
//    SmartClient が引換券を JWT に交換する。パスワードは不要。
// =============================================================================
app.MapPost("/token/exchange", (ExchangeRequest req) =>
{
    // ★一度きり：取り出すと同時に消す（同じ券を二度使えない）
    if (req.Ticket is null || !tickets.TryRemove(req.Ticket, out var entry))
    {
        Console.WriteLine("[SSO ] 交換失敗: 引換券が無効または使用済み");
        return Results.Unauthorized();
    }
    if (DateTime.UtcNow > entry.ExpiresUtc)
    {
        Console.WriteLine("[SSO ] 交換失敗: 引換券が期限切れ");
        return Results.Unauthorized();
    }

    return IssueTokensFor(entry.Uid, "引換券");
});

// =============================================================================
//  【共通】POST /token/refresh
//    JWT が切れたとき、再ログインせずに新しい JWT を得る。
//    SmartClient は長時間動くため、5分ごとに再起動させるわけにいかない。
// =============================================================================
app.MapPost("/token/refresh", (RefreshRequest req) =>
{
    // ★使い捨て＋再発行（ローテーション）：盗まれた古い券は使えなくなる
    if (req.RefreshToken is null || !refreshs.TryRemove(req.RefreshToken, out var entry))
    {
        Console.WriteLine("[REF ] 更新失敗: リフレッシュトークンが無効または使用済み");
        return Results.Unauthorized();
    }
    if (DateTime.UtcNow > entry.ExpiresUtc)
    {
        Console.WriteLine("[REF ] 更新失敗: リフレッシュトークンが期限切れ");
        return Results.Unauthorized();
    }

    return IssueTokensFor(entry.Uid, "リフレッシュ");
});

// =============================================================================
//  【従来経路】POST /token … ユーザー名/パスワードで認証（ステップ4・5と同じ）
//    本番では SSO 経路に一本化して閉じるのが望ましい。ここでは比較のため残す。
// =============================================================================
app.MapPost("/token", (LoginRequest req) =>
{
    LdapUser? user;
    try
    {
        user = LdapAuthenticate(req.Username, req.Password);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[LDAP] エラー: {ex.Message}");
        return Results.Problem($"LDAP 接続または検索に失敗しました: {ex.Message}", statusCode: 503);
    }
    if (user is null)
        return Results.Unauthorized();

    return IssueTokensFor(user.Uid, "パスワード");
});

// -----------------------------------------------------------------------------
// 保護された API（ステップ5と同じ）
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
}).RequireAuthorization();

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

app.MapGet("/", () => "JwtDemoServer (step6/SSO bridge) is running.");

app.Run();


// =============================================================================
//  指定 uid の利用者について、LDAP から属性・ロールを解決して
//  アクセストークン（JWT）とリフレッシュトークンを発行する。
//  ★パスワードは検証しない。ここに来た時点で「本人確認は済んでいる」前提。
//    SSO 経路＝SP が確認済み／リフレッシュ経路＝過去に確認済み。
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
        Console.WriteLine($"[LDAP] エラー: {ex.Message}");
        return Results.Problem($"属性の解決に失敗しました: {ex.Message}", statusCode: 503);
    }

    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Subject),
        new("uid", user.Uid),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new("purpose", "smartclient-demo"),
        new("auth_via", via),   // どの経路で得たトークンかを記録（監査に有用）
    };
    if (user.Mail is not null)
        claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Mail));
    foreach (var r in roles)
        claims.Add(new Claim(RoleClaim, r));

    var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer:             Issuer,
        audience:           Audience,
        claims:             claims,
        notBefore:          DateTime.UtcNow,
        expires:            DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
        signingCredentials: creds);
    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    // リフレッシュトークンを新規発行（使い捨て・ローテーション）
    var refresh = NewSecret();
    refreshs[refresh] = new StoreEntry(user.Uid, DateTime.UtcNow.AddHours(RefreshTokenHours));

    Console.WriteLine($"[JWT ] {user.Uid} にトークンを発行（経路: {via}／ロール: " +
                      $"{(roles.Length == 0 ? "なし" : string.Join(", ", roles))}）");

    return Results.Ok(new
    {
        access_token  = jwt,
        token_type    = "Bearer",
        expires_in    = AccessTokenMinutes * 60,
        refresh_token = refresh
    });
}

// 推測不能な秘密値（引換券・リフレッシュトークン用）
static string NewSecret() =>
    Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

// =============================================================================
//  ClaimsPrincipal から表示用の情報を取り出す（多候補で拾う）
// =============================================================================
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
//  LDAP：識別子（uid でも mail でも可）から利用者を引く。パスワードは検証しない。
//  ★REMOTE_USER は 01PLM01@plm-lab.local（メール形式）で来るため、
//    uid と mail の両方で探せるようにしてある。
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
    var request = new SearchRequest(LdapBaseDn, filter, SearchScope.Subtree,
                                    LdapLoginAttr, LdapMailAttr);
    var response = (SearchResponse)conn.SendRequest(request);

    if (response.Entries.Count != 1) return null;

    var entry = response.Entries[0];
    var uid  = entry.Attributes.Contains(LdapLoginAttr)
                 ? entry.Attributes[LdapLoginAttr][0]?.ToString() ?? identifier : identifier;
    var mail = entry.Attributes.Contains(LdapMailAttr)
                 ? entry.Attributes[LdapMailAttr][0]?.ToString() : null;

    return new LdapUser(uid, mail, entry.DistinguishedName);
}

// =============================================================================
//  LDAP：検索 → 本人バインドでパスワードを検証（従来経路用）
// =============================================================================
LdapUser? LdapAuthenticate(string username, string password)
{
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return null;

    var user = LdapLookup(username);
    if (user is null) return null;

    var id = new LdapDirectoryIdentifier(LdapHost, LdapPort);
    using var verifyConn = new LdapConnection(id) { AuthType = AuthType.Basic };
    verifyConn.SessionOptions.ProtocolVersion = 3;
    verifyConn.Credential = new NetworkCredential(user.Dn, password);
    try
    {
        verifyConn.Bind();
    }
    catch (LdapException)
    {
        return null;   // パスワード不一致
    }
    return user;
}

// =============================================================================
//  LDAP：利用者 DN を member に持つグループを検索し、cn をロール名として返す
// =============================================================================
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

// LDAP 検索フィルタ用のエスケープ（RFC4515）
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

// LDAP から得た利用者情報。Subject は JWT の sub に入れる値（メール優先）。
record LdapUser(string Uid, string? Mail, string Dn)
{
    public string Subject => Mail ?? Uid;
}

// 引換券・リフレッシュトークンの保管内容
record StoreEntry(string Uid, DateTime ExpiresUtc);

// リクエストボディの型
record LoginRequest(string Username, string Password);
record ExchangeRequest(string? Ticket);
record RefreshRequest(string? RefreshToken);
