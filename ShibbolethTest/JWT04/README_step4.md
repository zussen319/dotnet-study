# JWT 認証デモ ― ステップ4：初回認証を ApacheDS(LDAP) に接続

これまで `/token` の初回認証は「固定ユーザー辞書」でした。ステップ4では、これを **Shibboleth Windows 版手順書で構築した ApacheDS（LDAP）への認証**に差し替えます。これにより、JWT の発行対象が SSO と同じユーザー源になり、両経路（.aspx=SSO ／ SmartClient=JWT）が同じディレクトリで認証されるようになります。

あわせて、**識別子の統一**（JWT の `sub` を SSO 側 `REMOTE_USER` と揃える）をここで確定します。

- **変更するのはサーバだけ**（`JwtDemoServer.cs` と `JwtDemoServer.csproj`）。
- **クライアント（ステップ3）は変更なし**。ただし表示される識別子が変わります（後述）。

---

> ## 実機での確認結果（重要・このステップで判明した点）
>
> **1. LDAP 接続設定は下記で確定**（Apache Directory Studio で実機確認済み。詳細は「事前確認」節）
>
> ```csharp
> const string LdapHost      = "localhost";
> const int    LdapPort      = 10389;
> const string LdapBindDn    = "uid=idp-reader,ou=people,dc=example,dc=com";
> const string LdapBindPass  = "idp-reader";
> const string LdapBaseDn    = "ou=people,dc=example,dc=com";
> const string LdapLoginAttr = "uid";
> const string LdapMailAttr  = "mail";
> ```
>
> 検索用アカウントは README 初版のデフォルト（`uid=admin,ou=system`）ではなく、**Shibboleth IdP 用に作成済みの読み取り専用アカウント `idp-reader` を流用**した。管理者権限を使わない分、本番構成（最小権限）に近い。
>
> **2. `userPassword` は SSHA ハッシュで保存されている**（Studio 上では `SSHA hashed password` と表示され、平文は見えない）。本デモは userPassword を読んで自前照合するのではなく、**利用者 DN＋入力パスワードで bind し、サーバ側にハッシュ照合させる**方式のため、SSHA でも問題なく動作する。
>
> **3. `email` クレームが `null` になる（.NET 10 で確認）**
> whoami の応答で `username` は取れるのに `"email": null` となる。原因は LDAP ではなく **.NET のクレーム名の変換**。応答の `claims` 配列を見ると、`sub` に入れた値が `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` という URI 形式になっている。
> - ステップ1で追加した `JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear()` は**効いていない**。.NET 8 以降の JwtBearer は既定で `JsonWebTokenHandler` を使うため、`JwtSecurityTokenHandler` 側のマップを消しても影響しないため。
> - `username` は多候補フォールバックのおかげで拾えていたが、`email` は `"email"` 一択だったため null になった。
> - **対処**：`email` も多候補で拾う。
>   ```csharp
>   var email = user.FindFirstValue(JwtRegisteredClaimNames.Email)   // "email"
>               ?? user.FindFirstValue(ClaimTypes.Email);            // URI に変換された場合
>   ```
>   （**ステップ5のコードに反映済み**。ステップ4のコードを直す場合は上記を適用する。）
> - 教訓：**.NET のクレーム名変換は起こる前提で、取り出しは常に多候補で行う**。ステップ1で得た知見がここでも再現した形。

---

## 事前確認（ここが最重要）

コードを動かす前に、ApacheDS 側の実際の値を確認します。**Apache Directory Studio** で `localhost:10389` に接続して確認します。

### 確認項目と実機の値（確認済み）

| 項目 | コードの定数 | **実機で確認した値** | 備考 |
|---|---|---|---|
| 接続先 | `LdapHost` / `LdapPort` | `localhost` / `10389` | 平文ポート |
| 検索用アカウント | `LdapBindDn` / `LdapBindPass` | `uid=idp-reader,ou=people,dc=example,dc=com` / `idp-reader` | IdP 用リーダーを流用 |
| 利用者の起点 | `LdapBaseDn` | `ou=people,dc=example,dc=com` | 配下に `01PLM01` / `01PLM02` / `idp-reader` の3件 |
| ログイン属性 | `LdapLoginAttr` | `uid` | 値は `01PLM01` |
| メール属性 | `LdapMailAttr` | `mail` | 値は `01PLM01@plm-lab.local` |

利用者エントリの構成（Studio で確認）：

```
DN: uid=01PLM01,ou=people,dc=example,dc=com
  objectClass : inetOrgPerson / organizationalPerson / person / top
  cn          : Test User 01PLM01
  sn          : 01PLM01
  mail        : 01PLM01@plm-lab.local
  uid         : 01PLM01
  userPassword: SSHA hashed password（平文は見えない）
```

### パスワードについて

本環境のアカウントはすべて **Joe アカウント（ユーザー名＝パスワード）** で構築している。したがって：

- `01PLM01` のパスワードは `01PLM01` → クライアントの `const string Password = "01PLM01";` は**変更不要**。
- `idp-reader` のパスワードは `idp-reader`。

---

## 参考：Apache Directory Studio での bind テスト手順

「その DN とパスワードで本当に認証できるか」を、コードを動かす前に確認する手順です。ここでいう **bind テスト**とは、指定した DN とパスワードで LDAP に接続（認証）できるかを試すことで、接続ウィザードの **「Check Authentication」** ボタンがその機能そのものです。`userPassword` が SSHA ハッシュでも、サーバ側が照合するので問題ありません。

**コードの (1)(2) がやっていることと同じ**なので、ここが通ればコードも通ります。

### A. 利用者（01PLM01）を確認する ― 使い捨ての新規接続で試す

1. 画面左下の「**Connections**」パネルで右クリック →「**New Connection**」（またはツールバーの新規接続アイコン）。
2. 1ページ目「**Network Parameter**」を入力：
   - Connection name：`bindtest-01PLM01`（任意・後で消してよい）
   - Hostname：`localhost` ／ Port：`10389`
   - Encryption method：「**No encryption**」（10389 は平文のため）
   - 「**Check Network Parameter**」を押し、成功することを確認。
3. 「Next >」で「**Authentication**」ページへ：
   - Authentication Method：「**Simple Authentication**」
   - Bind DN or user：`uid=01PLM01,ou=people,dc=example,dc=com`
   - Bind password：`01PLM01`
4. 「**Check Authentication**」を押す。**成功メッセージが出れば bind 成功＝パスワード一致**。確認が目的なので「Cancel」で閉じてよい。

失敗（invalid credentials 等）が出る場合は、DN の綴りかパスワードを見直します。

### B. 検索用アカウント（idp-reader）を確認する ― 既存接続を使う

左下に **`plm-lab-idp-reader`** という接続が既にあるので、これを使えば新規作成は不要です。

1. 「Connections」パネルで `plm-lab-idp-reader` を右クリック →「**Properties**」。
2. 左の一覧から「**Authentication**」を選択。
3. Bind DN が `uid=idp-reader,ou=people,dc=example,dc=com`、Bind password が `idp-reader` であることを確認（空なら入力）。
4. 「**Check Authentication**」を押して成功を確認。

（分かりにくければ、A と同じ要領で `bindtest-idp-reader` という新規接続を作って確認しても同じです。）

---

## 変更点の要点

- **検索 → 本人バインド 方式**：まず検索用アカウントで利用者を検索して DN を得て、その DN＋入力パスワードで**もう一度バインド**してパスワードを検証します。Shibboleth IdP が LDAP に対して行う認証と同じ考え方です。
- **識別子の統一**：LDAP から得た `mail` を JWT の `sub` に採用（SSO の `REMOTE_USER` と同じメール形式）。併せて `uid`（正規化した識別番号）と `email` も別クレームで持たせます。
- **401 と 503 の区別**：パスワード不一致/未存在は `401`、LDAP に繋がらない等のサーバ側事情は `503`。
- **空パスワードの排除**：DN 付き・空パスワードの simple bind は「未認証バインド」と解釈され、サーバによっては検証を素通りします。明示的に弾いています（LDAP の典型的な落とし穴）。
- **LDAP インジェクション対策**：検索フィルタに入れる前に入力をエスケープ（RFC4515）。

---

## セットアップ

`JwtDemoServer.csproj` に LDAP ライブラリ `System.DirectoryServices.Protocols` を追加します。Visual Studio の「NuGet パッケージの管理」から追加しても同じです。

```powershell
cd JwtDemoServer
dotnet restore
```

## 実行方法

1. **ApacheDS を起動**しておく（Windows サービス）。
2. サーバを起動：`cd JwtDemoServer` → `dotnet run`。起動ログに `[LDAP] 認証先: ...` が出ます。
3. 別ターミナルでクライアントを実行：`cd JwtDemoClient` → `dotnet run`。

## 実行結果（実機）

クライアント（ステップ3のまま）の動きは同じですが、**`username` がメール形式になります**（`sub` に `mail` を採用したため）。

```
[1] whoami を3回連続で呼びます（トークンは1回だけ取得されるはず）
        （/token でトークン取得。約300秒有効。取得回数=1）
    1回目: 成功 (200) username=01PLM01@plm-lab.local
    2回目: 成功 (200) username=01PLM01@plm-lab.local
    3回目: 成功 (200) username=01PLM01@plm-lab.local
    → /token を呼んだ回数: 1（1 が期待値＝使い回せている）
[2] トークンを強制的に期限切れにして呼びます
        （/token でトークン取得。約300秒有効。取得回数=2）
    結果: 成功 (200) username=01PLM01@plm-lab.local
[3] キャッシュを壊れたトークンに差し替えて呼びます
        （401 を受信 → トークンを取り直して再試行します）
    結果: 成功 (200) username=01PLM01@plm-lab.local
=== 完了 ===
```

`username` が `01PLM01@plm-lab.local` になっていれば、**LDAP の `mail` が JWT に載り、SSO の `REMOTE_USER` と同じ形で取り出せている**ことの確認になります。

### PowerShell での詳細確認

```powershell
$login = @{ username = "01PLM01"; password = "01PLM01" } | ConvertTo-Json
$res   = Invoke-RestMethod -Uri http://localhost:5000/token -Method Post -Body $login -ContentType "application/json"
Invoke-RestMethod -Uri http://localhost:5000/api/whoami -Headers @{ Authorization = "Bearer $($res.access_token)" } | ConvertTo-Json -Depth 5
```

実機の応答（抜粋）：

```json
{
  "username": "01PLM01@plm-lab.local",
  "uid":      "01PLM01",
  "email":    null,
  "purpose":  "smartclient-demo",
  "claims": [
    { "type": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
      "value": "01PLM01@plm-lab.local" },
    { "type": "uid", "value": "01PLM01" }
  ]
}
```

`email` が null になる理由と対処は、冒頭の「実機での確認結果 3」を参照。`claims` 配列に実際のクレーム型名が出るので、変換の有無を目で確認できます。

- **誤パスワード → 401**：`password="wrong"` にすると 401（LDAP のパスワード検証で弾かれる）。
- **ApacheDS 停止中 → 503**：サービスを止めて叩くと 503（認証失敗ではなく接続不可として区別される）。

---

## コードの見どころ（JwtDemoServer.cs）

- **`LdapAuthenticate(...)`**：(1) 検索用アカウントでバインド→利用者検索、(2) 見つけた DN＋入力パスワードでバインド→検証、の2段構え。成功なら `(uid, mail)`、失敗なら `null`、接続不可などは例外で呼び出し側へ。
- **`/token` の claims 構築**：`sub = mail ?? uid`、加えて `uid`・`email`・`purpose`。
- **`EscapeLdapFilter`**：検索フィルタ用のエスケープ。ユーザー入力をそのままフィルタに埋めない。

---

## 識別子の統一（設計判断のメモ）

`sub` に何を入れるかは、2経路を合流させる上での要の判断です。本デモは **メール形式（`mail`）を `sub` に採用**しました。理由は、SSO 側が emailAddress 形式の `REMOTE_USER`（`01PLM01@plm-lab.local`）を渡す構成であり、さらに本番の Entra ID もメール/UPN 形式を送るため、**両経路・本番で同じ形に揃う**からです。PLM 側は、どちらの経路でも同じ正規化（`@` の前を取り出す等）を一律に適用すればよくなります。

- 別案として `sub = uid`（`01PLM01`）にする手もありますが、その場合は SSO 側で `@` を落とす正規化が前提になり、経路ごとに扱いが分かれます。
- 本デモは「トークンに両方入れる」ことで、当面どちらでも選べるようにしています。最終的にどちらを正とするかは、PLM の既存の識別子設計に合わせて決めてください。

---

## 本番化に向けたメモ（随時追記）

- ✅ **識別子の統一（本ステップで対応）**：`sub`=メール形式で SSO/Entra と一致。`uid` も併記。
- ✅ **検索用アカウントの最小権限（本ステップで対応）**：管理者ではなく読み取り専用の `idp-reader` を使用。
- **クレーム名の変換に注意**：.NET は標準クレーム名を URI 形式へ変換することがある。**取り出しは常に多候補で**行う（本ステップで `email` が null になる形で再現）。
- **LDAP の暗号化**：デモは平文 `10389`。本番は **LDAPS(636) か StartTLS** にし、資格情報が平文で流れないようにする（SSO で TLS 化したのと同じ）。
- **接続の堅牢化**：タイムアウト設定、接続の再利用/プール、リトライ方針を検討（デモは都度接続）。
- **HTTPS 必須**（据え置き）：Bearer トークンは HTTPS で運ぶ。
- **鍵/シークレット管理**（据え置き）：署名鍵・LDAP バインド情報は設定/シークレットストアへ。
- **【ステップ6で扱う】トークンの入手経路**：SSO 認証済みのメイン画面から SmartClient へ再ログインなしでトークンを渡す“橋渡し”。
- **【ステップ6で扱う】HS256↔RS256**：検証が別サービスに分かれる／Entra 発行に寄せる段階で非対称鍵を検討。

---

## 次のステップ（予定）

- **ステップ5**：JWT に**ロール/属性**を載せ、それに応じて API 側で処理・アクセス制御を分岐。LDAP のグループから取得し、SSO 側の属性解放（`attribute-filter`）との対応も意識する。
- **ステップ6（統合の要）**：SSO 認証済みのメイン画面(.aspx)が JWT を発行/取得して SmartClient に引き渡す“橋渡し”。
- **ステップ7（発展）**：検証済み API が業務データ（Oracle 19c：`scott/tiger`・EMP 等）をユーザー権限で返す。
