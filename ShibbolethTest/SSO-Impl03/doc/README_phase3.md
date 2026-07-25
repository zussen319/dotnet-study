# フェーズ3：引換券の発行と JWT の交換

フェーズ2で「SSO 認証済みの利用者が誰か」を .aspx が知り、API は SSO なしで到達できる土台ができました。フェーズ3では、その上に**2つの経路をつなぐ橋渡し**を実装します。

**このフェーズが完了すると、一連の流れが実環境で通ります。**

```
ブラウザで SSO 認証
    ↓  REMOTE_USER 確定（SP がセット・詐称不可）
メイン画面が引換券を発行
    ↓  起動リンクに埋め込む（一度きり・60秒）
SmartClient が起動
    ↓  引換券を JWT に交換（パスワード入力なし）
保護 API を呼ぶ
    ↓  JWT を検証
利用者が確定（= ブラウザ側の REMOTE_USER と同じ値）
```

> ⚠️ **本フェーズは提案版です。** 実行して判明した点は、これまで同様この冒頭に追記して確定版にしてください。

---

## ステップ6の設計からの重要な改善

学習フェーズ（ステップ6）では、`.aspx` が HTTP でトークンサービスを呼び、`X-Remote-User` ヘッダーで利用者を伝える形にしていました。そして「**ヘッダーは詐称できるので本番では絶対にこのままにしないこと**」と強く注意していました。

**今回の1アプリ構成では、この危険性がそもそも存在しません。**

`.aspx` と Web API が同じアプリケーションなので、`Default.aspx` は `TicketStore.Issue()` を**同一プロセス内で直接呼びます**。HTTP を経由しないため、ヘッダーを信用する余地がありません。REMOTE_USER は SP がセットしたものを直接読んでおり、クライアントからは詐称できません。

```
[ステップ6の学習構成]           [フェーズ3の実装]
.aspx ──HTTP(X-Remote-User)──> API      .aspx ──直接呼び出し──> TicketStore
        ↑ ここが詐称の余地                       ↑ 同一プロセス・詐称の余地なし
```

**4.8 に統一して1アプリにしたことの、思わぬ副次的な効果**です。

---

## フェーズ移行時の作業（フォルダをコピーして進める場合）

各フェーズを別フォルダに分けて進める場合（例：`SSO-Impl02` → `SSO-Impl03`）の手順です。**動作する状態がフェーズごとにスナップショットとして残る**ため、問題が起きたときに直前のフェーズと比較できる利点があります。ステップ1〜7を `JWT01`〜`JWT07` に分けたのと同じ考え方です。

### 移行チェックリスト

**(1) フォルダをコピーする**

```
SSO-Impl02\PlmSsoDemo\  →  SSO-Impl03\PlmSsoDemo\
```

**(2) 先にフェーズ2の状態で動作確認する**（重要）

フェーズ3のファイルを適用する**前に**、コピーしただけの状態で動作確認してください。

- `https://sp.plm-lab.local/PLM/Default.aspx` … REMOTE_USER が表示される
- `https://sp.plm-lab.local/PLM/api/ping` … JSON が返る

こうしておくと、後で問題が出たときに「**コピーの問題**」と「**フェーズ3の実装の問題**」を切り分けられます。

**(3) IIS の物理パスを付け替える**（2箇所）

| 対象 | 新しい物理パス |
|---|---|
| アプリケーション `/PLM` | `...\SSO-Impl03\PlmSsoDemo\PlmSsoDemo.Web` |
| 仮想ディレクトリ `/PLM/smartclient` | `...\SSO-Impl03\PlmSsoDemo\PlmSsoDemo.SmartClient\publish\smartclient` |

**(4) フォルダの権限を設定し直す**（新しいフォルダには引き継がれません）

| フォルダ | 必要な権限 |
|---|---|
| ソースフォルダ（`PlmSsoDemo.Web`） | `IIS_IUSRS` に**読み取りと実行** |
| `PlmSsoDemo.Web\App_Data` | `IIS_IUSRS` に**書き込み**（フェーズ3のログ出力用・**新規**） |

**(5) Web.config は「追記」する**

コピーした Web.config にはフェーズ2の設定が入っています。フェーズ3の `appSettings` は**置き換えではなく追記**してください。`DevRemoteUser` はコメントアウトのままにします。

**(6) Shibboleth の設定は変更不要**

`shibboleth2.xml` は URL ベースの設定で、ファイルシステムのパスには依存していません。

### ⚠️ ClickOnce の発行バージョンに注意

**最も事故りやすい箇所です。** オンライン専用の ClickOnce は、起動のたびに配置マニフェストのバージョンを確認し、**キャッシュ済みのものと同じなら再ダウンロードしません**。

フォルダをコピーした状態で同じバージョン（例：1.0.0.0）のまま発行すると、**コードを変更したのに古いアプリが起動する**という紛らわしい事象が起きます。

- 発行時に**バージョンが上がっていること**を確認する（「自動的にリビジョンを増やす」が有効なら通常は自動）
- それでも古い挙動が続く場合は、ClickOnce のキャッシュを消す

```
rundll32 dfshim CleanOnlineAppCache
```

### 参考：ソリューションファイルの形式

VS2026 で新規作成すると、ソリューションが **`.slnx`**（新しい XML 形式）になる場合があります。**この形式は VS2019 では開けません。**

検証環境では問題ありませんが、**組織へ成果物を渡す段階では従来形式の `.sln` への変換が必要**です。VS で「ファイル」→「名前を付けて保存」から従来形式を選ぶか、新規に `.sln` を作ってプロジェクトを追加し直します。

プロジェクトファイル（`.csproj`）自体は旧形式なので問題ありません。**組織展開の準備段階で対応すれば十分**です。

---

## 成果物

### Web アプリ（`PlmSsoDemo.Web`）

| ファイル | 配置先 | 内容 |
|---|---|---|
| `Services/TicketStore.cs` | `Services\` | 引換券の発行・引き換え（一度きり・短命） |
| `Services/JwtHelper.cs` | `Services\` | JWT の発行・検証 |
| `Services/AppLog.cs` | `Services\` | ファイルへのログ出力 |
| `Controllers/TokenController.cs` | `Controllers\` | `POST /PLM/api/token/exchange` |
| `Controllers/WhoAmIController.cs` | `Controllers\` | `GET /PLM/api/whoami`（JWT 保護） |
| `Controllers/DiagController.cs` | `Controllers\` | `GET /PLM/api/diag`（診断） |
| `Default.aspx` / `.cs` / `.designer.cs` | プロジェクト直下 | 引換券の発行と起動リンク |
| `webconfig-appsettings-phase3.xml` | （参考） | Web.config に追加する appSettings |

### SmartClient（`PlmSsoDemo.SmartClient`）

| ファイル | 内容 |
|---|---|
| `Program.cs` | 引換券 → JWT → API 呼び出し |

---

## 手順

### 1. NuGet パッケージを追加する（Web アプリ）

`PlmSsoDemo.Web` を右クリック →「NuGet パッケージの管理」→「参照」タブで次を検索して導入します。

- **`System.IdentityModel.Tokens.Jwt`**

.NET Framework 4.8 では **6.x 系**が使えます（4.6.1 以上が対象）。依存する `Microsoft.IdentityModel.*` も自動で入ります。

> 8.x 以降を選ぶと、より新しい .NET Framework が要求される場合があります。導入に失敗する場合はバージョンを下げてください。

### 2. 参照を追加する（SmartClient）

`PlmSsoDemo.SmartClient` の「参照」を右クリック →「参照の追加」→「アセンブリ」から次を追加します。

- **`System.Deployment`** … ClickOnce の起動情報（フェーズ1から継続）
- **`System.Web.Extensions`** … JSON の解析（`JavaScriptSerializer`）

`System.Web.Extensions` が**今回の新規追加**です。追加しないとビルドできません。

### 3. ファイルを配置する

上の表のとおりに配置します。`Services` フォルダは新規作成してください。

> ⚠️ **エクスプローラーでコピーしただけではプロジェクトに含まれません。** ソリューションエクスプローラーの「すべてのファイルを表示」→ 対象ファイルを右クリック →「**プロジェクトに含める**」を忘れずに。フェーズ2で `PingController.cs` が見つからなかったのと同じ原因になります。

`Default.aspx` は **`.aspx` / `.cs` / `.designer.cs` の3点セット**で差し替えてください（コントロールが増えているため）。

### 4. Web.config に設定を追加する

`webconfig-appsettings-phase3.xml` の内容を、既存の `<appSettings>` に追加します。他の節は変更不要です。

**特に重要なのが署名鍵です。**

```xml
<add key="JwtSigningKey" value="plm-sso-demo-signing-key-change-me-32bytes!" />
```

HS256 は 32 バイト以上が必要で、短いと起動時にエラーになります。デモ用の値なので、実際には推測されない値に変更してください。フェーズ4で Windows 資格情報マネージャーからの取得に差し替えます。

**`DevRemoteUser` はコメントアウトしたままにしてください。** SSO の動作確認を行うためです。

### 5. ログ出力先の権限を設定する

ログは `App_Data\logs\` に出力されます。**アプリケーションプールの ID に書き込み権限が必要**です。

`PlmSsoDemo.Web\App_Data` フォルダのプロパティ →「セキュリティ」→ `IIS_IUSRS` に「**変更**」または「**書き込み**」を許可してください。

権限が無い場合もアプリは動作しますが（ログを諦めるだけ）、切り分けの手段が減るので設定を推奨します。

### 6. ClickOnce を再発行する

SmartClient のコードを変更したため、再発行が必要です。設定はフェーズ2から変更ありません。

- インストール URL：`https://sp.plm-lab.local/PLM/smartclient/`
- インストールモード：**オンラインのみ**
- 「オプション」→「マニフェスト」→ ☑ **URL パラメーターをアプリケーションに渡すことを許可する**

---

## 動作確認

### (1) メイン画面で引換券が発行されること

```
https://sp.plm-lab.local/PLM/Default.aspx
```

SSO ログイン後、次が表示されることを確認します。

- 「✓ SSO 認証済み」／ REMOTE_USER = `01PLM01@plm-lab.local`
- **「SmartClient を起動」ボタン**が表示される
- 引換券（先頭8文字）と「60 秒（一度きり）」の表示

### (2) SmartClient が起動し、JWT で API を呼べること【本フェーズの核心】

「**SmartClient を起動**」をクリックします。**パスワードは一切求められないはずです。**

期待される表示：

```
=== PLM SmartClient（フェーズ3）===

[1] 起動パラメータを確認します
    接続先   : https://sp.plm-lab.local/PLM
    引換券   : AbCdEfGh...

[2] 引換券を JWT に交換します（POST /api/token/exchange）
    ✓ 交換成功（パスワード入力なし）
      利用者     : 01PLM01@plm-lab.local
      JWT        : eyJhbGciOiJI...
      有効期間   : 1800 秒

[3] JWT を付けて保護 API を呼びます（GET /api/whoami）
    ✓ 成功（200）
      user       : 01PLM01@plm-lab.local
      authVia    : sso-ticket
      serverTime : 2026-07-20 15:30:00

      ★ここに表示された user は、ブラウザ画面の REMOTE_USER と同じ値です。
        SSO で認証した利用者として、API を呼べています。

[4] 検証が効いていることを確認します
  (4-a) 同じ引換券をもう一度使う → 拒否されるはず
        → 401 Unauthorized（認証されていない）（期待どおり。引換券は一度きり）
  (4-b) トークン無しで API を呼ぶ → 401 になるはず
        → 401 Unauthorized（認証されていない）（期待どおり）
  (4-c) 改ざんしたトークンで呼ぶ → 401 になるはず
        → 401 Unauthorized（認証されていない）（期待どおり。署名検証に失敗）

=== 完了 ===
```

**確認の勘所は3点です。**

1. **パスワードがどこにも出てこない**のに、利用者が確定している
2. **[3] の `user` が、ブラウザ画面の REMOTE_USER と一致**している（2経路が同じ識別子で合流）
3. **[4] で3種類とも 401** になる（引換券の使い回し・トークン無し・改ざんを、それぞれ弾いている）

### (3) 二重起動が拒否されること

SmartClient を閉じ、**ブラウザを再読み込みせずに**もう一度「SmartClient を起動」をクリックします。

ブラウザのキャッシュから同じ引換券で起動されるため、**[2] で交換に失敗**するはずです。これは正しい動作です。画面を再読み込みすれば新しい引換券が発行され、再び起動できます。

### (4) 診断エンドポイント

```
https://sp.plm-lab.local/PLM/api/diag
```

設定値と内部状態が JSON で返ります。署名鍵そのものは返さず「設定されているか」だけを表示します。`ticket.currentCount` で未使用の引換券の数も確認できます。

### (5) ログの確認

```
PlmSsoDemo.Web\App_Data\logs\plmsso-yyyyMMdd.log
```

一連の流れが時刻付きで記録されています。

```
2026-07-20 15:29:55.123 [TICKET] 発行 user=01PLM01@plm-lab.local ticket=AbCdEfGh... 有効=60秒 保持数=1  <- GET /PLM/Default.aspx from 192.168.1.10
2026-07-20 15:30:01.456 [API] 交換要求 ticket=AbCdEfGh...  <- POST /PLM/api/token/exchange from 192.168.1.10
2026-07-20 15:30:01.460 [TICKET] 引き換え成功 user=01PLM01@plm-lab.local ticket=AbCdEfGh... 残り保持数=0
2026-07-20 15:30:01.470 [JWT] 発行 sub=01PLM01@plm-lab.local 有効=30分
2026-07-20 15:30:01.480 [API] 交換成功 user=01PLM01@plm-lab.local
2026-07-20 15:30:02.100 [API] whoami 成功 sub=01PLM01@plm-lab.local
```

**デバッガをアタッチしなくても処理の流れが追えます。** うまくいかない場合は、まずこのログを確認してください。

---

## Visual Studio でのデバッグ実行（SmartClient ⇔ Web サーバの往復を追う）

引換券の交換から JWT 発行までの**往復の処理**を、サーバー側・クライアント側の両方でステップ実行して確認できます。

### 手順

**(1) サーバー側にアタッチする**

フェーズ2で確定した「`w3wp.exe` にアタッチ」方式です。

1. ブラウザのプライベートウィンドウで `https://sp.plm-lab.local/PLM/Default.aspx` を開く（SSO 認証を通し、`w3wp.exe` を起動させる）
2. VS を管理者として起動し、`PlmSsoDemo.Web` を開く
3. 「デバッグ」→「プロセスにアタッチ」→「すべてのユーザーのプロセスを表示する」→ **`w3wp.exe`**
4. `TokenController.Exchange` と `TicketStore.Consume`、必要なら `JwtHelper.Issue` にブレークポイントを置く

**(2) 本物の引換券を取得する**

> ⚠️ **`--ticket=TESTTICKET` のような固定値では、交換の成功は確認できません。** `TESTTICKET` はサーバーに実在しない引換券のため、`TicketStore.Consume` で「無効か使用済み」と判定され 401 になります。**実際に発行された引換券が必要**です。

ブラウザの `Default.aspx` で「**SmartClient を起動**」リンクを**右クリック →「リンクのアドレスをコピー」**します。画面表示は先頭8文字だけですが、リンクのアドレスには `?ticket=` の後に**完全な値**が入っています。

```
https://sp.plm-lab.local/PLM/smartclient/PlmSsoDemo.SmartClient.application?ticket=＜完全な値＞
```

**(3) SmartClient のコマンドライン引数に貼る**

`PlmSsoDemo.SmartClient` のプロパティ →「デバッグ」→「コマンドライン引数」に、`ticket=` 以降の値を指定します。

```
--ticket=＜(2)でコピーした完全な値＞
```

**(4) 60秒以内に「Start New Instance」で実行する**

`PlmSsoDemo.SmartClient` を右クリック →「デバッグ」→「新しいインスタンスの開始」。**引換券は60秒で失効・一度きり**なので、貼り付けたら手早く実行します。SmartClient 側の `ExchangeTicket()` にもブレークポイントを置けば、応答を受け取る様子まで追えます。

これで「SmartClient が引換券を送信 → サーバーが `Consume` で照合 → `Issue` で JWT 発行 → SmartClient が受信」という往復の全体を、ステップ実行で確認できます。

### 「新しいインスタンスの開始」で出るダイアログについて（実機で確認）

次の警告が表示されますが、**OK で問題ありません。**

```
The security debugging option is set but it requires the Visual Studio hosting process
which is unavailable in this debugging configuration. The security debugging option will
be disabled. ...
```

これは **ClickOnce アプリ特有の警告**です。部分信頼で動く前提の「セキュリティデバッグ」機能が、この構成では使えないため無効化して続行する、という意味で、**引換券の交換や API 呼び出しのデバッグには影響しません**。毎回出るのが煩わしければ、プロパティ →「セキュリティ」→「ClickOnce セキュリティを有効にする」のチェックを外すと消えます（外さなくても支障はありません）。

### 引換券が一度きりであることを実感できる

一度デバッグ実行に成功した後、**同じコマンドライン引数のまま再実行すると 401 になります**。`TicketStore.Consume` の `TryRemove` で券が消える瞬間にブレークポイントを置くと、この動きがはっきり見えます。再実行するにはブラウザを再読み込みして新しい引換券を取り直します。これは動作確認 (3)「二重起動の拒否」と同じ仕組みを、デバッガ越しに見ていることになります。

### デバッグを楽にする2つの工夫（実機の要望から追加）

**(a) デバッグ中はプロパティが編集できない点への対処**

VS がデバッグ実行状態のとき、プロジェクトのプロパティは編集不可になります（VS の仕様）。そのため「サーバー側にアタッチしてから SmartClient のコマンドライン引数を編集する」ことはできません。次のいずれかで対応します。

- **順序を変える**：先に SmartClient のコマンドライン引数を入れてから、サーバー側にアタッチする
- **VS を2つ起動する**：一方を `PlmSsoDemo.Web`（サーバー側アタッチ）、もう一方を `PlmSsoDemo.SmartClient` 用にする
- **下記 (c) の入力ダイアログを使う**：そもそもコマンドライン引数を触らずに済む

**(b) 引換券の有効期限を延ばす**

デバッグ作業に60秒では短い場合、Web.config で延ばせます。

```xml
<add key="TicketLifetimeSeconds" value="300" />
```

画面の有効期限表示（`Default.aspx`）もこの値に自動追従します。

> ⚠️ **これは開発環境での一時的な緩和です。** 有効期限が長いほど、引換券が漏れたときの悪用の余地が広がります。動作確認が終わったら短い値（60秒程度）に戻してください。

**(c) デバッグ実行時の引換券入力ダイアログ**

`SmartClient` は、**DEBUG ビルドで、かつ引換券がどこからも得られない場合に限り**、引換券を手入力するダイアログを表示します。

- コマンドライン引数の貼り替えが不要になり、プロパティの編集可否も気にせずに済みます
- URL 全体（`...?ticket=...`）を貼っても、`ticket=` 以降を自動で取り出します
- **`#if DEBUG` で囲っているため、本番の Release ビルドには一切含まれません**。ClickOnce 起動や引数指定がある通常の起動でもダイアログは出ないため、本番の動作には影響しません

使い方は、サーバー側にアタッチした状態で SmartClient をデバッグ実行し（引数は不要）、出てきたダイアログにブラウザからコピーした引換券を貼り付けて OK、というだけです。

---

## トラブルシューティング

| 症状 | 原因の候補 | 対処 |
|---|---|---|
| ビルドエラー（`Microsoft.IdentityModel` が見つからない） | NuGet 未導入 | 手順1を実施 |
| ビルドエラー（`JavaScriptSerializer` が見つからない） | `System.Web.Extensions` 参照漏れ | 手順2を実施 |
| `/PLM/api/token/exchange` が 404 | コントローラーがプロジェクト未登録 | 「プロジェクトに含める」を確認 |
| 起動直後に「引換券がありません」 | ブラウザから起動していない／`TrustUrlParameters` 未設定 | フェーズ1の設定を確認 |
| [2] で 401「引換券が無効か、既に使用済み」 | 二度目の起動、または60秒超過 | ブラウザを**再読み込み**して新しい引換券で起動 |
| [2] で「通信できませんでした」 | 証明書が信頼されていない／URL 誤り | ルート CA の信頼を確認。診断で接続先 URL を確認 |
| [3] で 401「署名が不正です」 | 署名鍵の不一致（発行後に変更した等） | `Web.config` の `JwtSigningKey` を確認し、引換券から取り直す |
| 起動時に「JwtSigningKey が設定されていません」 | Web.config への追加漏れ | 手順4を実施 |
| ログファイルができない | `App_Data` の書き込み権限不足 | 手順5を実施 |
| `/PLM/api/diag` が 404 | `DiagEnabled` が false | Web.config で `true` に |
| デバッグ実行で `--ticket=TESTTICKET` を使うと 401 | 固定値はサーバーに実在しない引換券 | 上記「Visual Studio でのデバッグ実行」の手順で**本物の引換券**を使う |
| 「新しいインスタンスの開始」で security debugging の警告 | ClickOnce アプリ特有の警告 | **OK で問題なし**（デバッグに影響しない） |

デバッグの詳しい手順は、上記の「**Visual Studio でのデバッグ実行**」の節を参照してください。`TokenController.Exchange` や `TicketStore.Consume` にブレークポイントを置くと、引き換えの様子を追えます。

---

## この構成の位置づけ（本番への対応）

| フェーズ3の実装 | 本番 PLM での対応 |
|---|---|
| `Default.aspx` | PLM のメイン画面（`whoami.asp` 相当） |
| `TicketStore`（プロセス内） | 複数台構成なら共有ストア（DB / Redis 等）へ |
| `JwtSigningKey`（Web.config） | **フェーズ4で Windows 資格情報マネージャーへ** |
| `WhoAmIController` | SmartClient 向けの実際の業務 Web サービス |
| JWT の `sub` | PLM の利用者識別子（`@` の前を切り出す等の正規化は PLM 側の責務） |

**IdP を顧客の Entra ID に差し替えても、この構成は変わりません。** SP が `REMOTE_USER` を確定するという前提が同じだからです。

---

## 残っている課題（フェーズ4以降）

- 🔺 **署名鍵の保管**（フェーズ4）：Web.config の固定値 → Windows 資格情報マネージャー
- **JWT の有効期限切れ**：現在は再起動が必要。実運用では期限設計かリフレッシュの検討が必要
- **引換券ストアの共有化**：Web サーバ複数台の場合
- **HTTPS の徹底**：現構成は既に HTTPS のみだが、HTTP バインドを塞ぐかの検討
- **診断エンドポイントの無効化**：確認後は `DiagEnabled` を false に

---

## 次のステップ

**フェーズ4：署名鍵を Windows 資格情報マネージャーへ**

- アプリケーションプール専用のローカルアカウントを作成
- そのアカウントで資格情報を登録
- `JwtHelper.SigningKey` を資格情報マネージャー参照に差し替え
- Web.config から署名鍵を削除

フェーズ3が動いていれば、**変更するのは鍵の取得元だけ**です。動作が変わらないことをもって、移行の成功を確認できます。
