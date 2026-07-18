# JWT 認証デモ ― ステップ5：ロール／属性による認可

ステップ4で「**誰なのか**（認証／authn）」を LDAP で確定しました。ステップ5では「**何をしてよいのか**（認可／authz）」を扱います。LDAP のグループから利用者のロールを取得して JWT に載せ、それに応じて API のアクセスを分岐させます。

**このステップの最大の学びは、401 と 403 の区別**です。

| 応答 | 意味 | 問題の種類 |
|---|---|---|
| **401** Unauthorized | 誰だか分からない（トークンが無い/無効/期限切れ） | **認証**の問題 |
| **403** Forbidden | 誰かは分かるが、権限が足りない | **認可**の問題 |

SSO 側との対応で言えば、Shibboleth IdP が LDAP から属性を解決し `attribute-filter` で SP へ解放するのと、ここでの「LDAP のグループ → JWT の role クレーム」は同じ位置づけです。**どちらの経路でも、認可の材料はディレクトリから来る**という構図になります。

- **変更するのはサーバとクライアントの両方**。
- **LDAP 側にグループを追加する作業が必要**です（下記「事前準備」）。

> ⚠️ **本ステップは提案版です。** 実行して判明した点は、これまで同様この冒頭に追記して確定版にしてください。

---

## 成果物

| ファイル | 配置先 | 内容 |
|---|---|---|
| `plm-groups.ldif` | 任意の場所（インポート用） | ApacheDS に追加するグループ定義 |
| `JwtDemoServer.cs` | `JWT05\JwtDemoServer\` | ロール取得と認可を追加 |
| `JwtDemoClient.cs` | `JWT05\JwtDemoClient\` | 2人の利用者で違いを確認 |

`.csproj` はステップ4から変更ありません（`System.DirectoryServices.Protocols` を参照済みのもの）。ソリューションはこれまで同様、`JWT04` をコピーして `JWT05` を作る形で構いません。

---

## 事前準備：ApacheDS にグループを作る

現在の ApacheDS には `ou=people` しかなく、グループがありません。ロールの元になるグループを追加します。

作るのは次の構成です。

```
ou=groups,dc=example,dc=com
  ├─ cn=plm-admins  … 管理者ロール（member: 01PLM01）
  └─ cn=plm-users   … 一般利用者ロール（member: 01PLM01, 01PLM02）
```

**01PLM01 は両方に所属（ロール2つ）、01PLM02 は plm-users のみ**。この差が、認可の分岐を確認する材料になります。

### Apache Directory Studio での LDIF インポート手順

1. Studio で **`plm-lab-admin`** の接続を開きます（グループ作成は書き込み操作なので、読み取り専用の idp-reader ではなく管理者接続を使います）。
2. LDAP Browser のツリーで **`dc=example,dc=com`** を右クリック →「**Import**」→「**LDIF Import...**」を選びます。
3. 「LDIF File」に `plm-groups.ldif` のパスを指定し、「**Finish**」。
4. ツリーで `dc=example,dc=com` を右クリック →「**Reload**」（または F5）して、**`ou=groups`** が現れることを確認します。
5. `cn=plm-admins` をクリックし、右ペインに `member: uid=01PLM01,ou=people,dc=example,dc=com` があることを確認します。

> **うまくいかない場合**：エラーが出たら、Studio 下部の「Modification Logs」「Error Log」タブにメッセージが出ます。「Entry Already Exists」なら既に作成済みなので問題ありません。手作業で作りたい場合は、`dc=example,dc=com` を右クリック →「New」→「New Entry」から、objectClass に `organizationalUnit`（ou=groups）、`groupOfNames`（cn=plm-admins 等）を選んで作成することもできます。

> **補足**：`groupOfNames` は **member を最低1つ必須**とするスキーマです。メンバーが空のグループは作れないため、LDIF では最初からメンバーを入れてあります。

### 検索用アカウントの権限について

ロール検索は `idp-reader` で行います。ApacheDS の既定では認証済みユーザーは読み取り可能なので、そのまま動くはずです。もし `ou=groups` が検索できずロールが空になる場合は、Studio の `plm-lab-idp-reader` 接続で `ou=groups` が見えるかを確認してください（見えなければ ACI の調整が必要ですが、学習用途では一時的に admin バインドへ切り替えても構いません）。

---

## 変更点の要点（サーバ）

- **ロールの取得**：`LdapFindRoles(userDn)` が `(&(objectClass=groupOfNames)(member=<利用者DN>))` で検索し、ヒットしたグループの `cn` をロール名として返します。「グループ側から利用者を探す」向きの検索です。
- **JWT への格納**：`role` クレームを**複数**追加します（同名クレームを複数入れると JWT では配列になる）。
- **`RoleClaimType` の明示**：`TokenValidationParameters` で `RoleClaimType = "role"` を指定します。これがないと `RequireRole` が既定の URI 形式クレームを探してしまい、**ロールを入れたのに 403 になる**という分かりにくい失敗をします。
- **ポリシーで名前を分離**：`AdminOnly` / `PlmUser` というポリシー名を定義し、エンドポイントにはポリシー名を付けます。LDAP のグループ名が変わっても、エンドポイント側を書き換えずに済みます。
- **クレーム名変換の対策を両方に適用**：ステップ4の知見を受け、`JwtSecurityTokenHandler` と `JsonWebTokenHandler` の**両方**の `DefaultInboundClaimTypeMap` をクリアします。これで `email` も正しく取れるようになります（取り出し側の多候補も維持）。

### 追加した API

| エンドポイント | 必要な権限 | 01PLM01（管理者） | 01PLM02（一般） |
|---|---|---|---|
| `GET /api/whoami` | 認証のみ | 200 | 200 |
| `GET /api/parts` | plm-users または plm-admins | 200 | 200 |
| `POST /api/parts` | **plm-admins のみ** | 200 | **403** |
| `GET /api/report` | 認証のみ（**中身で分岐**） | 原価あり | 原価なし |

`/api/report` は「弾く」のではなく「**返す内容を変える**」例です。PLM では、同じ画面でも役割によって表示項目が変わることが多いので、この形も実際によく使います。

---

## 実行方法

1. **ApacheDS を起動**し、上記のグループ追加を済ませておく。
2. サーバ起動：`cd JwtDemoServer` → `dotnet run`。起動ログに `[LDAP] 認証先: ... group=ou=groups,...` が出ます。
3. 別ターミナルでクライアント実行：`cd JwtDemoClient` → `dotnet run`。

サーバ側のコンソールにも、認証のたびに `[LDAP] 01PLM01 のロール: plm-admins, plm-users` のようなログが出ます。**ロールが正しく取れているかは、まずこのログで確認**するのが早道です。

## 期待される実行結果

```
=== JWT 認証デモ クライアント（ステップ5：ロールによる認可）===
接続先: http://localhost:5000

────────────────────────────────────────
■ 01PLM01 として実行（管理者ロールを持つ想定）
────────────────────────────────────────
[1] GET /api/whoami（認証のみ必要）
    → 成功 (200) user=01PLM01@plm-lab.local roles=[plm-admins, plm-users]
[2] GET /api/parts（plm-users または plm-admins が必要）
    → 成功 (200) user=01PLM01@plm-lab.local
[3] POST /api/parts（plm-admins のみ）
    → 成功 (200) user=01PLM01@plm-lab.local
[4] GET /api/report（ロールに応じて内容が変わる）
    → 成功 (200) user=01PLM01@plm-lab.local → 管理者向けレポート（原価情報を含む）

────────────────────────────────────────
■ 01PLM02 として実行（一般利用者の想定）
────────────────────────────────────────
[1] GET /api/whoami（認証のみ必要）
    → 成功 (200) user=01PLM02@plm-lab.local roles=[plm-users]
[2] GET /api/parts（plm-users または plm-admins が必要）
    → 成功 (200) user=01PLM02@plm-lab.local
[3] POST /api/parts（plm-admins のみ）
    → 拒否 (403 Forbidden) … 権限が足りない（認証は通っている）
[4] GET /api/report（ロールに応じて内容が変わる）
    → 成功 (200) user=01PLM02@plm-lab.local → 一般向けレポート（原価情報なし）

────────────────────────────────────────
■ 参考：トークン無しで呼ぶ（401 になるはず）
    → 401 Unauthorized（誰だか分からない＝認証の問題）

=== 完了 ===
```

**確認の勘所は [3] の行**です。同じ API を呼んでいるのに、01PLM01 は 200、01PLM02 は **403**。ここが「認証は通っているが認可で弾かれた」状態で、トークン無しの **401** とは別物であることを見比べてください。

### PowerShell での詳細確認（任意）

```powershell
# 01PLM02（一般利用者）でトークンを取り、管理者専用 API を叩く
$login = @{ username = "01PLM02"; password = "01PLM02" } | ConvertTo-Json
$res   = Invoke-RestMethod -Uri http://localhost:5000/token -Method Post -Body $login -ContentType "application/json"
$h     = @{ Authorization = "Bearer $($res.access_token)" }

Invoke-RestMethod -Uri http://localhost:5000/api/whoami -Headers $h | ConvertTo-Json -Depth 5
try { Invoke-RestMethod -Uri http://localhost:5000/api/parts -Method Post -Headers $h }
catch { "拒否された（期待どおり）: $($_.Exception.Response.StatusCode)" }   # → Forbidden
```

**JWT の中身も覗いてみる**と、`role` が配列で入っていることが確認できます（ステップ1の手順と同じ要領）。

```powershell
$payload = $res.access_token.Split(".")[1].Replace('-','+').Replace('_','/')
switch ($payload.Length % 4) { 2 { $payload += "==" } 3 { $payload += "=" } }
[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
# → sub, uid, email, role(...) が見える
```

---

## トラブルシューティング

| 症状 | 原因の候補 | 対処 |
|---|---|---|
| `roles=[(なし)]` になる | `ou=groups` が未作成／member の DN が実際の利用者 DN と一致していない | Studio で `cn=plm-admins` の `member` の値を確認。`uid=01PLM01,ou=people,dc=example,dc=com` と完全一致しているか |
| ロールはあるのに全部 403 | `RoleClaimType` の指定漏れ、またはロール名の綴り違い | サーバの `RoleClaimType = RoleClaim` と、ポリシーの `RequireRole("plm-admins")` の綴りを確認 |
| 401 が返る | LDAP のパスワード不一致 | Joe アカウント（ユーザー名＝パスワード）になっているか。Studio の Check Authentication で確認 |
| 503 が返る | ApacheDS 停止、または検索用アカウントの設定ミス | サービス稼働と `idp-reader` のバインドを確認 |

---

## SSO 側との対応関係（設計メモ）

| SmartClient 経路（JWT） | ブラウザ経路（SSO） |
|---|---|
| LDAP のグループを検索 | IdP が LDAP から属性を解決 |
| `role` クレームを JWT に載せる | `attribute-filter.xml` で SP へ属性を解放 |
| API が `RequireRole` で判定 | アプリが SP 経由の属性を見て判定 |

**本番（Entra ID）では、ロールの供給元が Entra 側のグループ/アプリロールに変わります**。SAML アサーションのグループ要求として送られてくるので、SP 側でそれを受け取る形になります。JWT 側も、ステップ6で発行の主体が変わればロールの出どころが変わります。いずれにせよ「**ディレクトリが認可の材料を供給し、アプリは受け取って判定する**」という構図は同じです。

---

## 本番化に向けたメモ（随時追記）

- ✅ **識別子の統一**（ステップ4）／✅ **検索用アカウントの最小権限**（ステップ4）
- ✅ **クレーム名の変換対策**（本ステップ）：`JsonWebTokenHandler` 側もクリアし、取り出しは多候補で。
- **ロールの粒度と命名**：デモは `plm-admins`/`plm-users` の2段階。本番は PLM の既存の権限体系（部門・プロジェクト・製品別など）にどう対応させるかを設計する必要がある。**ロール名をそのまま API に埋めず、ポリシー名で分離**しておくと移行が楽（本ステップで実践）。
- **ロールの鮮度**：JWT にロールを載せる方式では、**トークンの有効期限が切れるまで古い権限が残る**。デモは5分なので実害は小さいが、本番の有効期限設計では「権限剥奪がいつ効くか」を意識する。
- **ネストしたグループ**：groupOfNames の入れ子（グループがグループの member）はこのコードでは辿らない。必要なら再帰検索を検討。
- **LDAP の暗号化**（据え置き）／**HTTPS 必須**（据え置き）／**鍵・シークレット管理**（据え置き）
- **【ステップ6で扱う】トークンの入手経路**：SSO 認証済みのメイン画面から SmartClient へ再ログインなしでトークンを渡す“橋渡し”。
- **【ステップ6で扱う】HS256↔RS256**：発行者と検証者が分かれる段階で非対称鍵を検討。

---

## 次のステップ（予定）

- **ステップ6（統合の要）**：SSO 認証済みのメイン画面(.aspx)が JWT を発行/取得して SmartClient に引き渡す“橋渡し”。ここまでのデモは SmartClient が自分でユーザー名/パスワードを送っていたが、本番の利用者は既に SSO 認証済みで、**2度目の入力をさせるわけにいかない**。`REMOTE_USER` を信頼して JWT を発行する仕組みと、その安全な受け渡し（短命の引換券方式など）を扱う。あわせて HS256↔RS256 と署名者の設計を整理する。
- **ステップ7（発展）**：検証済み API が業務データ（Oracle 19c：`scott/tiger`・EMP 等）をユーザー権限で返す。ステップ5のロールを、実際のデータ絞り込みに使う。
