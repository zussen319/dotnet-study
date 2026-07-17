# JWT 認証デモ ― ステップ1：サーバ（発行・検証の最小 API）

SmartClient のような「ブラウザ外のクライアント」を JWT で認証する仕組みを、最小構成で体験するためのデモです。
ステップ1では**サーバだけ**を作ります（クライアントは次のステップで追加）。動作確認は PowerShell で行います。

> **実機での確認結果（重要・このステップで判明した2点）**
> 1. **ポートは 5000**：`launchSettings.json` は環境によって読み込まれず、その場合 ASP.NET Core の既定ポート **5000** で起動する。本デモは **5000 で進める**（`launchSettings.json` は不要・削除してよい）。ポートを固定したい場合のみ、`Program.cs` に `builder.WebHost.UseUrls("http://localhost:5000");` を1行足す。
> 2. **`sub` クレームの取り出し**：.NET の JWT ハンドラは標準クレーム `sub` を URI 形式（`ClaimTypes.NameIdentifier`）に変換することがある（.NET 10 で確認）。`"sub"` だけを探すと見つからず `username` が `(unknown)` になる。**複数の候補を順に探す**ことで、変換の有無にかかわらず確実に取得できる（`Program.cs` に反映済み）。

## これは何をするものか

エンドポイントは2つです。

| エンドポイント | 認証 | 役割 |
|---|---|---|
| `POST /token` | 不要 | ユーザー名/パスワードを検証し、正しければ **JWT を発行** |
| `GET /api/whoami` | **必要** | リクエストの JWT を**検証**し、トークン内のユーザー名を返す |

JWT の本質＝「**発行 → 付与して送信 → 検証 → 結果返却**」を、この2つで再現します。

- 署名方式：**HS256**（1つの秘密鍵で署名も検証も行う。デモ向けの最もシンプルな方式）
- 有効期限：**5分**（期限切れの挙動を試しやすいよう短め）
- ユーザー：**固定**（`01PLM01`/`01PLM01`、`01PLM02`/`01PLM02`）。後のステップで LDAP 認証に差し替え

## 実行方法（VS2026 または コマンドライン）

### コマンドラインの場合

```powershell
cd JwtDemoServer
dotnet run
```

初回は NuGet パッケージの復元が走ります。`Now listening on: http://localhost:5000` と出れば起動成功です（`launchSettings.json` を効かせて 5080 等にしていない限り、既定は 5000）。

### Visual Studio 2026 の場合

`JwtDemoServer.csproj` を開いて実行（F5 またはデバッグなしの実行）。既定では `http://localhost:5000` で起動します（`launchSettings.json` が読み込まれない場合）。

## 動作確認（別の PowerShell ウィンドウで）

サーバを起動したまま、**別の PowerShell**を開いて順に実行します。

### 1) サーバが起きているか

```powershell
Invoke-RestMethod http://localhost:5000/
# → "JwtDemoServer is running. ..." が返れば OK
# （5000 で繋がらない場合は、サーバ起動ログの "Now listening on:" のポートに合わせる）
```

### 2) トークンを発行してもらう（POST /token）

```powershell
$login = @{ username = "01PLM01"; password = "01PLM01" } | ConvertTo-Json
$res = Invoke-RestMethod -Uri http://localhost:5000/token -Method Post -Body $login -ContentType "application/json"
$res
$token = $res.access_token
"取得したトークン: $token"
```

`access_token`（長い文字列）が返ってくれば発行成功です。この文字列が JWT です。

> **JWT の中身を覗いてみる（任意）**：JWT は「ヘッダー.ペイロード.署名」を `.` で繋いだ形。ペイロード部は Base64URL エンコードされた JSON です（暗号化ではないので誰でも読めます。だからこそ「署名」で改ざんを防ぐ、という設計）。
> ```powershell
> $payload = $token.Split(".")[1].Replace('-','+').Replace('_','/')
> switch ($payload.Length % 4) { 2 { $payload += "==" } 3 { $payload += "=" } }
> [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
> # → sub=01PLM01, iss=JwtDemoServer, exp=... などが見える
> ```

### 3) トークンを付けて保護 API を呼ぶ（GET /api/whoami）

```powershell
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Uri http://localhost:5000/api/whoami -Headers $headers | ConvertTo-Json -Depth 5
# → message / username=01PLM01 / purpose=smartclient-demo / claims(...) が返れば検証成功
#    ※ username が (unknown) の場合は sub の取り出し（複数候補方式）を確認（下記「コードの見どころ」）
```

### 4) 検証が効いていることの確認（ここが重要）

**トークン無しだと拒否される**：

```powershell
try { Invoke-RestMethod http://localhost:5000/api/whoami }
catch { "拒否された（期待どおり）: $($_.Exception.Response.StatusCode)" }
# → 401 Unauthorized
```

**改ざんされたトークンは拒否される**（末尾を書き換えて署名を壊す）：

```powershell
$tampered = $token.Substring(0, $token.Length - 2) + "xx"
try { Invoke-RestMethod http://localhost:5000/api/whoami -Headers @{ Authorization = "Bearer $tampered" } }
catch { "改ざんは拒否された（期待どおり）: $($_.Exception.Response.StatusCode)" }
# → 401 Unauthorized（署名検証に失敗するため）
```

**間違ったパスワードではトークンが発行されない**：

```powershell
$bad = @{ username = "01PLM01"; password = "wrong" } | ConvertTo-Json
try { Invoke-RestMethod -Uri http://localhost:5000/token -Method Post -Body $bad -ContentType "application/json" }
catch { "認証失敗（期待どおり）: $($_.Exception.Response.StatusCode)" }
# → 401 Unauthorized
```

**（任意）有効期限切れの確認**：トークン取得後 5 分待ってから 3) を実行すると、`401` になります（`exp` を過ぎたため）。

## 確認できること

- **発行**：正しい資格情報 → JWT が発行される。誤り → 401（発行されない）。
- **検証**：正しい JWT → API が結果を返す。無し/改ざん/期限切れ → 401。
- **ステートレス**：サーバはトークンを保存していない。トークン内の署名だけで正しさを判定している。
- **クレームの取り出し**：検証後、トークン内の `sub`（ユーザー識別子）を取り出して `username` として返す（＝ SmartClient 版の REMOTE_USER）。

## コードの見どころ（Program.cs）

- `AddJwtBearer(...)` の `TokenValidationParameters` … **何を検証するか**（署名・発行者・利用者・期限）。
- `POST /token` … 資格情報チェック → クレーム（`sub` 等）を詰めて署名 → JWT を返す。
- `GET /api/whoami` に付けた `.RequireAuthorization()` … これが「**有効な JWT が無ければ 401**」を実現している。
- `ClaimsPrincipal user` から `sub` を取り出している … これが SmartClient 版の「REMOTE_USER 相当」。
  - **`sub` の取り出しは複数候補で行う**：`FindFirstValue("sub")` → `FindFirstValue(ClaimTypes.NameIdentifier)` → `Identity?.Name` の順。.NET が `sub` を URI 形式に変換しても確実に拾える。レスポンスの `claims` 配列に**実際のクレーム型名と値**を出しているので、変換の有無を目で確認できる（デバッグに有用）。

## 次のステップ（予定）

1. **ステップ2**：C# コンソールの「SmartClient 役」を作り、`/token` 取得 →`/api/whoami` 呼び出しの往復をプログラムで再現。
2. **ステップ3**：期限切れ・改ざん・トークン無しの挙動をクライアント側からも確認。
3. **ステップ4**：`/token` の初回認証を、今回構築した **LDAP（ApacheDS）** に接続して 01PLM01 等で認証（SSO 環境と統合）。
4. **ステップ5**：JWT にロール/属性を入れ、それに応じて処理を分岐。
5. **（発展）業務データとの連携**：検証後の API が、ユーザーに紐づく業務データを返す。検証環境の Oracle 19c（`scott/tiger`・EMP テーブル等）を使えば、「JWT で認証したユーザーが、自分の権限で業務データを取得する」という PLM 本来の流れに近いデモにできる。
