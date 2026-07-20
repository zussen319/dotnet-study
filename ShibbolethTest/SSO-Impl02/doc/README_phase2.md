# フェーズ2：IIS のサイト構成と SP のパス単位保護（/PLM 配下版）

フェーズ1で ClickOnce の引数受け渡しが確認できたので、次は**土台となる Web アプリと IIS の構成**を整えます。

このフェーズのゴールは3つです。

1. **SP 保護下の .aspx から `REMOTE_USER` が取得できる**
2. **`/PLM/api` が SP 保護の対象外**になっている（SmartClient が SSO なしで呼べる）
3. **組織と同じ方法で Visual Studio デバッグができる**（ご要望(7)）

引換券や JWT はまだ扱いません。**「認証済みの利用者が誰かを .aspx が知り、API は SSO なしで到達できる」という土台**を先に固めます。ここが全体の要になります。

---

> ## 実機での確認結果（本フェーズは全項目クリア・確定版）
>
> **動作確認4項目はすべて期待どおり。** デバッグ手段も確保できた（方式は当初案から変更）。
>
> | 確認項目 | 結果 |
> |---|---|
> | (1) メイン画面の SSO 保護と REMOTE_USER 取得 | ✅ `01PLM01@plm-lab.local` を取得。`mail` 属性も届いている |
> | (2) `/PLM/api/ping` が SP 保護対象外 | ✅ リダイレクトされず JSON が返る |
> | (3) ClickOnce の引数受け渡し（再発行後） | ✅ フェーズ1と同じ結果 |
> | (4) 既存ページ（whoami.asp）への影響 | ✅ 影響なし |
>
> **判明した点は5件。いずれも詳細を本文に記載。**
>
> 1. **VS の個別コンポーネントが必要**（→ 手順0）：ワークロードだけでは .NET Framework のテンプレートが出ない。`.NET Framework project and item templates` を追加。
> 2. **言語バージョンは自動選択のままでよい**（→ 手順0-2）：.NET Framework では自動的に C# 7.3 になり、VS2019 互換。
> 3. **`.aspx` の文字コード対策が必要**（→ 手順2-2）：日本語ロケールでない Windows では `.aspx` の日本語が化ける。`<globalization fileEncoding="utf-8" ... />` と BOM 付き保存で解決。
> 4. **起動ページは ClickOnce の発行先に配布されない**（→ 手順6）：Web アプリ側に置く。結果的に本番の構造と一致する。
> 5. **F5 デバッグは成立しない。アタッチ方式に統一**（→ デバッグ実行の節）：SSO 環境固有の3つの壁があり、`w3wp.exe` へのアタッチで確定。
>
> **その他、つまずきやすい点**
> - `Default.aspx.designer.cs` の差し替え漏れ → ビルドエラー（`The name 'xxx' does not exist`）
> - `Controllers\PingController.cs` のプロジェクト未登録 → `No type was found that matches the controller named 'ping'`

---

---

## 構成：`/PLM/` 配下にまとめる

PLM システムへの適用を見据え、**すべて `/PLM/` 配下**に配置します。役割分担が URL から読み取れるようになります。

```
https://sp.plm-lab.local/
    ├─ whoami.asp                  … 既存の SSO 検証ページ（そのまま）  【SP 保護あり】
    └─ /PLM/                       … PLM アプリケーション（ASP.NET 4.8）
         ├─ Default.aspx           … メイン画面                        【SP 保護あり】
         ├─ /PLM/api/ping          … Web API（トークンサービス）        【SP 保護なし】
         └─ /PLM/smartclient/      … ClickOnce 配布                    【SP 保護なし】
```

4.8 に統一したことで、**WebForms と Web API 2 を1つのアプリに同居**させられます。IIS のアプリケーションもアプリプールも1つで済みます。

**保護を「パス単位」で分けるのが設計の肝**です。SmartClient は SSO セッション（Cookie）を持たないため、`/PLM/api` を保護すると呼び出せません。ここは JWT が守ります（フェーズ3以降）。

### フォルダ構成（組織の開発スタイルに合わせる）

ソリューション配下のフォルダを IIS にマッピングする方式を前提にします。

```
C:\work\repos\...\PlmSsoDemo\
    PlmSsoDemo.sln
    PlmSsoDemo.Web\             ← IIS の /PLM にマッピング（アプリケーション）
    PlmSsoDemo.SmartClient\     ← WinForms（ClickOnce 発行元）
    publish\smartclient\        ← ClickOnce 発行先 → /PLM/smartclient にマッピング（仮想ディレクトリ）
```

ClickOnce の発行先をソース管理外の `publish\` に置き、そこを仮想ディレクトリで見せることで、**発行のたびにファイルをコピーする手間がなくなります**。

---

## 成果物

| ファイル | 配置先 | 内容 |
|---|---|---|
| `Default.aspx` / `Default.aspx.cs` / `Default.aspx.designer.cs` | プロジェクト直下 | メイン画面（REMOTE_USER 表示）。**designer も必ず差し替える** |
| `RemoteUserHelper.cs` | プロジェクト直下 | REMOTE_USER 取得＋開発モード対応 |
| `Controllers/PingController.cs` | `Controllers\` | `/PLM/api/ping` 疎通確認 |
| `App_Start/WebApiConfig.cs` | `App_Start\` | Web API のルーティング |
| `Global.asax.cs` | プロジェクト直下 | 起動時にルーティング登録 |
| `Web.config` | プロジェクト直下 | VS 生成版に3点を追加した完全版（差し替え可） |
| `SmartClient/Program.cs` | `PlmSsoDemo.SmartClient\` | SmartClient（フェーズ1から名前空間を変更） |
| `launch-test.html` | **Web アプリ側**（`PlmSsoDemo.Web\`） | 起動テストページ（後述の理由で Web アプリ側に置く） |

画面内のリンクは `ResolveUrl("~/api/ping")` のように**アプリケーション相対**で書いてあるため、`/PLM` 配下でもそのまま動きます。

---

## 手順

### 0. 事前確認：Visual Studio の個別コンポーネント（実機で判明）

「ASP.NET and web development」ワークロードをインストール済みでも、**.NET Framework 用のテンプレートは既定では含まれません**。「ASP.NET Web アプリケーション (.NET Framework)」が新規プロジェクト一覧に出てこない場合は、これが原因です。

Visual Studio Installer →「変更」→「**個別のコンポーネント**」タブで、次を追加してください。

- **`.NET Framework project and item templates`**（.NET Framework プロジェクトおよび項目テンプレート）

> **実機での確認結果**：VS2026 では、この1つを追加するだけでテンプレートが表示されました。参考情報でよく併記される「Additional project templates (previous versions)」は、VS2026 の一覧には**存在しませんでした**（不要）。
> なお本コンポーネントは **約1.6GB** を要します。既定で含まれないのはこのサイズが理由と見られます。

あわせて `.NET Framework 4.8 targeting pack` も確認しておきます。

### 0-2. 言語バージョンについて（実機で判明）

プロジェクトのプロパティ →「ビルド」→「詳細設定」の Language Version は **「Automatically selected based on framework version」のままで正解**です。グレーアウトして変更できませんが、これが正しい状態です。

.NET Framework プロジェクトでは、この自動選択の結果が **C# 7.3**（.NET Framework で完全にサポートされる最後の言語バージョン）になります。**何もしなくても VS2019 互換の範囲に収まる**ため、手を加える必要はありません。

> ⚠️ ここを手動で「最新」に変更しないでください。新しい構文が書けてしまい、組織の VS2019 でコンパイルできないコードが混入します。

### 1. プロジェクトを作る

Visual Studio 2026 で新規プロジェクトを作成します。

- テンプレート：「**ASP.NET Web アプリケーション (.NET Framework)**」（C#）
- プロジェクト名：`PlmSsoDemo.Web`
- フレームワーク：**.NET Framework 4.8**
- 次の画面で「**Web Forms**」を選択し、「**Web API**」のチェックも入れます
  - Web API のチェックで必要な NuGet（`Microsoft.AspNet.WebApi`）と `App_Start\WebApiConfig.cs` が入ります。入れ忘れた場合は後から `Microsoft.AspNet.WebApi.WebHost` を追加してください。
- 認証：「**認証なし**」（認証は Shibboleth SP が担当するため）

作成後、**言語バージョンを C# 7.3 に固定**します（プロパティ →「ビルド」→「詳細設定」）。組織の VS2019 で開けるようにするためです。

### 2. ファイルを配置し、Web.config を調整する

本フォルダのファイルを上の表のとおりに配置します。テンプレートが生成した同名ファイルは置き換えてください。名前空間は `PlmSsoDemo.Web` を前提にしています（プロジェクト名を変える場合は `Inherits="PlmSsoDemo.Web.Default"` も合わせて修正）。

> ⚠️ **`Default.aspx.designer.cs` も必ず差し替えてください。** WebForms のアプリケーションプロジェクトでは、`.aspx` 上の `runat="server"` コントロールは designer ファイルにフィールドとして宣言されます。`.aspx` だけを差し替えると designer が古いままになり、次のビルドエラーになります。
> ```
> The name 'phDevWarning' does not exist in the current context
> The name 'litAuthStatus' does not exist in the current context
> ...
> ```
> **後から `.aspx` にコントロールを追加した場合**も同様です。designer に同じ ID の宣言を足すか、プロジェクトを右クリック →「**Web アプリケーションに変換**」で再生成してください。

`Web.config` は同梱の完全版で差し替えられます（VS が生成したものに3点追加しただけで、`handlers` や `assemblyBinding` は生成されたままです）。追加しているのは次の3点です。

- **`DevRemoteUser`**（appSettings）… SP を経由しない環境で REMOTE_USER の代わりに使う値
- **`<authentication mode="None" />`** … 認証は SP が担当するため ASP.NET 側では無効に
- **`<customErrors mode="RemoteOnly" />`** … 診断しやすくするため

> **ClickOnce の MIME タイプは Web.config に書きません。** フェーズ1で IIS マネージャーに登録済みで、その設定が継承されるためです。ここで `<remove>` を書くと、対象が存在しない場合に HTTP 500.19 の構成エラーになることがあります。

> ⚠️ **`DevRemoteUser` は SSO を検証する際には必ず削除してください。** 残したままだと SSO を経由しなくても誰でもその利用者として振る舞えます。画面上部に警告が出るようにしてありますが、設定自体を消すのが確実です。

### 2-2. 文字コードの注意（実機で判明）

**`.aspx` 内の日本語が文字化けする場合があります。** 症状の特徴は「**画面の一部だけ**が化ける」ことです。`.aspx` に直接書いた見出しなどは化けるのに、コードビハインド（`.cs`）から出力した文字列は正しく表示されます。

原因は、**ASP.NET が `.aspx` ファイルを読むときの文字コード**です。既定ではサーバーのシステム既定コードページ（＝OS の言語設定）が使われるため、**日本語ロケールでない Windows では、UTF-8 で書かれた `.aspx` の日本語が別の文字として解釈されます**。`.cs` は C# コンパイラが UTF-8 として読むため化けません。この差が「一部だけ化ける」症状になります。

対処は2つあり、**両方**行うのが確実です。

1. **Web.config に文字コードを明示する**（同梱の Web.config に反映済み）
   ```xml
   <globalization fileEncoding="utf-8" requestEncoding="utf-8" responseEncoding="utf-8" />
   ```
   OS の言語設定に依存しなくなるため、開発機・顧客環境が日本語版でも英語版でも同じ結果になります。

2. **`.aspx` を UTF-8（BOM 付き）で保存する**（同梱ファイルは BOM 付きに変更済み）
   BOM があれば ASP.NET はそれを見て UTF-8 と判定します。Visual Studio で保存し直す場合は「ファイル」→「名前を付けて保存」→ 保存ボタンの横の▼→「エンコード付きで保存」→ **「Unicode (UTF-8 シグネチャ付き) - コードページ 65001」** を選びます。

> **これまでの LDIF・PowerShell と同じ「文字コードの経路」の問題**です。ただし .aspx は利用者に見せる画面なので、ASCII 化ではなく**文字コードを正しく指定して日本語を使う**のが適切です。

### 3. IIS にマッピングする

IIS マネージャーで、既存サイト（`sp.plm-lab.local`）の下に次を作成します。

**(a) アプリケーション `/PLM`**

- サイトを右クリック →「**アプリケーションの追加**」
- エイリアス：`PLM`
- 物理パス：`C:\work\repos\...\PlmSsoDemo\PlmSsoDemo.Web`（**ソースフォルダを直接指定**）
- アプリケーションプール：.NET CLR **v4.0** / **統合**パイプライン

**(b) 仮想ディレクトリ `/PLM/smartclient`**

- `/PLM` を右クリック →「**仮想ディレクトリの追加**」
- エイリアス：`smartclient`
- 物理パス：`C:\work\repos\...\PlmSsoDemo\publish\smartclient`

**(c) フォルダのアクセス権**

ソースフォルダを直接見せるため、**アプリケーションプールの ID に読み取り権限**が必要です。対象フォルダのプロパティ →「セキュリティ」で `IIS_IUSRS` に「読み取りと実行」を付与してください。これが無いと 500 エラーになります。

### 4. Shibboleth SP の保護範囲を更新する

`/PLM` 配下の構成に合わせ、除外パスを入れ子にします。フェーズ1でルート直下に書いた `<Path name="smartclient">` などは**不要になるので置き換え**てください。

```xml
<Host name="sp.plm-lab.local" authType="shibboleth" requireSession="true">
    <Path name="PLM">
        <!-- トークンサービス：JWT で保護するため SSO の保護は掛けない -->
        <Path name="api" authType="None" requireSession="false"/>
        <!-- ClickOnce 配布：SSO Cookie を持たないダウンローダーが取得するため保護から外す -->
        <Path name="smartclient" authType="None" requireSession="false"/>
    </Path>
</Host>
```

`<Path name="PLM">` は属性を書いていないため、親（`<Host>`）の `requireSession="true"` を引き継ぎます。つまり **`/PLM/Default.aspx` は保護され、その下の `api` と `smartclient` だけが除外**されます。

反映後は **Shibboleth Daemon サービスと IIS を再起動**します。

> **綴りについて**：URL のパスは通常は大文字小文字を区別しませんが、念のため `/PLM` の綴りは一貫させ、後述の動作確認で実際にリダイレクトの有無を確かめてください。

### 5. SmartClient プロジェクトを作る（フェーズ1からの引き継ぎ）

フェーズ1のスパイクを、正式なプロジェクト名で作り直します。**プロジェクト名の変更ではなく新規作成**を推奨します。VS のプロジェクト名変更では名前空間とアセンブリ名が自動的には追従せず、**ClickOnce のアプリケーション ID はアセンブリ名に紐づく**ため、中途半端な状態は原因の分かりにくい問題を招くためです。

1. ソリューションを右クリック →「追加」→「新しいプロジェクト」
2. テンプレート：「**Windows フォーム アプリ (.NET Framework)**」（C#）、フレームワーク **4.8**
3. プロジェクト名：`PlmSsoDemo.SmartClient`（ソリューション直下）
4. 生成された `Form1.*` を削除し、`SmartClient\Program.cs`（本フォルダ同梱）で置き換え
5. **参照に `System.Deployment` を追加**（フェーズ1と同じ。忘れるとビルドできません）

同梱の `Program.cs` は、名前空間を `PlmSsoDemo.SmartClient` に変更済みです。動作内容はフェーズ1のスパイクと同じ（起動情報と ticket の表示）で、フェーズ3でここに JWT の交換処理を追加していきます。

### 6. ClickOnce を再発行する

**配置場所とアプリ名が変わったため、再発行が必要です。** インストール URL は配置マニフェストに記録されており、実際の配信 URL と一致していないと起動・更新に失敗します。

- インストール URL：`https://sp.plm-lab.local/PLM/smartclient/`
- 発行場所：`C:\work\repos\...\PlmSsoDemo\publish\smartclient\`
- インストールモード：**オンラインのみ**
- 「オプション」→「マニフェスト」→ ☑ **URL パラメーターをアプリケーションに渡すことを許可する**（前回同様、必須）

発行物は `PlmSsoDemo.SmartClient.application` という名前になります。

> ### ⚠️ 起動ページ（`launch-test.html`）は発行先には置きません（実機で判明）
>
> **ClickOnce の発行では、起動ページは発行先のルートに配布されません。** プロジェクトにファイルを追加してビルドアクションを「コンテンツ」にしても、そのファイルは `Application Files\<アプリ名>_1_0_0_0\` の中に入ります。「アプリと一緒に配布されるデータファイル」という扱いだからです。
>
> **そこで起動ページは Web アプリ側（`PlmSsoDemo.Web`）に置きます。** 同梱の `launch-test.html` をプロジェクト直下に追加し、ビルドアクションを「コンテンツ」にしてください。リンクは `smartclient/PlmSsoDemo.SmartClient.application?ticket=...` という相対パスに修正済みです。
>
> **この配置は本番の構造そのものです。**
> ```
> https://sp.plm-lab.local/PLM/launch-test.html   ← 【SP 保護あり】起動ページ
> https://sp.plm-lab.local/PLM/smartclient/...     ← 【SP 保護なし】ClickOnce 本体
> ```
> 起動ページは SSO 保護下、ダウンロードは保護外。フェーズ3で起動リンクが `Default.aspx` に統合されても、この構造は変わりません。

---

## 動作確認

**ブラウザのプライベートウィンドウ**で確認するのが確実です（既存の SSO セッションの影響を受けないため）。

### (1) メイン画面 ― SSO 保護が効いていること

```
https://sp.plm-lab.local/PLM/Default.aspx
```

- **SSO のログイン画面にリダイレクトされる**こと
- ログイン後、次が表示されること
  - 「✓ SSO 認証済み」
  - **REMOTE_USER = `01PLM01@plm-lab.local`**
  - Shibboleth セッション「✓ あり」
  - サーバー変数の表に `Shib-Session-ID` や `mail` などが並ぶ

**このフェーズで最も重要な確認です。** ここで `REMOTE_USER` が取れれば、フェーズ3で引換券を発行する土台が整ったことになります。

### (2) API ― SSO 保護の対象外であること

```
https://sp.plm-lab.local/PLM/api/ping
```

- **SSO のログイン画面にリダイレクトされない**こと
- JSON が返り、`"remoteUser": null` と「SP 保護の対象外になっており、想定どおりです」が表示されること

**リダイレクトされたら、入れ子の `<Path>` が効いていません。** サービスの再起動漏れか、パスの綴りを確認してください。

### (3) ClickOnce ― 再発行後も引数が届くこと

```
https://sp.plm-lab.local/PLM/launch-test.html
```

（起動ページは Web アプリ側にあるため `/PLM/` 直下です。SSO 保護下なのでログインを求められます。）

フェーズ1と同じ3項目を再確認します。URL とアプリ名が変わっただけなので、同じ結果になるはずです。

### (4) 既存ページに影響が無いこと

```
https://sp.plm-lab.local/whoami.asp
```

従来どおり SSO 経由で表示されることを確認します。

---

## Visual Studio でのデバッグ実行（実機で検証・確定版）

> ### 結論
> **`w3wp.exe` へのアタッチ方式に統一する。** F5 による自動アタッチ（「ローカル IIS」での起動）は、この SSO 環境では成立しなかった。
> 組織の従来方式のうち「.aspx は F5、SmartClient 側はアタッチ」という使い分けは、**SP を導入した環境では「どちらもアタッチ」に一本化**するのが現実的。

組織の開発方式（**IIS を各開発機に導入し、ソリューション配下のフォルダを IIS にマッピング。VS は常に管理者権限で起動**）という土台はそのまま踏襲します。手順3でソースフォルダを直接マッピングしているのは、このためです。

### (A) .aspx 画面のデバッグ ― w3wp.exe にアタッチ【推奨・実機で確認済み】

1. **Visual Studio を管理者として起動**し、`PlmSsoDemo.Web` を開く
2. **ブラウザで先に** `https://sp.plm-lab.local/PLM/Default.aspx` を開く
   - SSO ログインまで済ませ、`w3wp.exe` を起動させておく
3. VS で「デバッグ」→「**プロセスにアタッチ**」
4. 「**すべてのユーザーのプロセスを表示する**」にチェック
5. **`w3wp.exe`** を選択して「アタッチ」
   - 「別のユーザーとして実行されているプロセスにアタッチしようとしています」という警告が出るが、**これは正常**。`w3wp.exe` はアプリプール ID で動いているため必ず出る。そのまま進める
6. ブラウザで画面を再読み込み → **ブレークポイントで停止**

**ソースフォルダを直接 IIS にマッピングしているため、コードを修正して保存すれば次のリクエストから反映されます**（WebForms は動的コンパイル）。F5 を押し直す必要がないぶん、慣れると軽快です。

**SP が動くフル IIS 上で実行されるため、SSO 込みでそのままデバッグできます。** `Page_Load` で `REMOTE_USER` が入った状態で停止することを実機で確認済みです。

### なぜ F5（ローカル IIS）が使えなかったか ― 実機での調査記録

同じ問題に当たったときのために、経緯を残しておきます。**3つの壁があり、いずれも SSO 環境固有のものです。**

**壁1：VS の「ローカル IIS」はホスト名 `localhost` しか受け付けない**

プロジェクト URL に `https://sp.plm-lab.local/PLM` を指定すると、次のエラーで拒否されます。

```
Cannot create the Web site 'https://sp.plm-lab.local/PLM'.
You must specify "localhost" for the server name.
```

`.csproj` の `<IISUrl>` を直接編集しても同じです。組織の環境で従来方式が動いているのは、プロジェクト URL が `localhost` になっているためと考えられます。**SSO 検証のために独自ホスト名を使ったことで、初めてこの制約に当たりました。**

**壁2：`localhost` でも SP 保護が掛かる**

回避策として Project Url を `http://localhost/PLM` にし、「**Override application root URL**」で `https://sp.plm-lab.local/PLM/` を開く構成にできます（設定自体は成功します）。

ただし、`localhost` は SP 保護の対象外にはなりません。`shibboleth2.xml` の `<ISAPI>` に次の対応表があるためです。

```xml
<Site id="1" name="sp.plm-lab.local" scheme="https" port="443"/>
```

**Windows 版の SP は、リクエストの Host ヘッダーではなく、この対応表の名前を使います。** つまり IIS のサイト ID 1 へのリクエストは、URL に何を入力しても一律 `sp.plm-lab.local` として扱われ、`<Host name="sp.plm-lab.local" requireSession="true"/>` が適用されます。`http://localhost/PLM/Default.aspx` を開くと SSO のログイン画面にリダイレクトされるのは、このためです。

**壁3：F5 が `0x80004005` で失敗する**

上記の設定で F5 を押すと、次のエラーが出ます。ブラウザは開いて SSO ログインまで進みますが、**ブレークポイントでは停止しません**。

```
Unable to start debugging on the web server. Operation not supported.
Unknown error: 0x80004005.
```

VS は IIS でデバッグを開始する際、対象 URL に `DEBUG` という特殊な HTTP 動詞のリクエストを送ってワーカープロセスと対話しますが、この事前ハンドシェイクが成立していないと考えられます。壁2のとおり `localhost` も SP 保護下にあるため、このリクエストが SSO のリダイレクト対象になっている可能性が高いです。

**試したが効果がなかった対処**：IIS に Windows 認証機能を導入し、サイトで「匿名認証：有効」「Windows 認証：有効」に設定 → **状況は変化しませんでした**（`0x80004005` のまま）。

**結論**：F5 の自動アタッチは VS と IIS 間の事前ハンドシェイクに依存するため、SP のような認証モジュールが介在すると成立しにくい構造です。一方アタッチ方式は**すでに動いているプロセスに後から接続するだけ**なので、認証の仕組みに影響されません。**アタッチ方式を標準とします。**

### アタッチ方式で「不要」な設定（実機で確認）

F5 を試す過程で行った設定は、**アタッチ方式では一切不要**です。アタッチ方式は「すでに動いているプロセスに後から接続するだけ」で、VS とサーバーの間に事前のやり取りが発生しないためです。

| 設定 | アタッチ方式で必要か | 対応 |
|---|---|---|
| IIS の **Windows 認証**を有効化 | **不要** | **元に戻す**（Windows の機能自体も外してよい） |
| VS プロパティの「Web」設定 | **不要** | 残しても実害なし（下記の注意点あり） |

**Windows 認証は元に戻すことを推奨します。** 効果がなかった変更を残す理由がないことに加え、認証の仕組みが1つ増えた状態だと、今後 API で予期しない 401 が出た際に切り分けの候補が増えます。また本番環境は Windows 認証を有効にしない構成になるため、検証環境を本番に近い状態に保つ意味もあります。

**VS の「Web」設定は残しても構いません**が、うっかり F5 を押すとエラーになります。混乱を避けるなら Start Action を「**Don't open a page. Wait for a request from an external application.**」にしておくと、アタッチ方式の運用と意味が一致します。

> ⚠️ **サーバーを「IIS Express」に戻すのは避けてください。** うっかり F5 を押すと IIS Express が起動し、**SP を経由しない状態**で画面が表示されてしまいます（`ERR_CONNECTION_REFUSED` やブレークポイントが止まらない原因になります）。

> **実機での確認**：VS の「Create Virtual Directory」実行後も、IIS 側の `/PLM` アプリケーションの構成（物理パス・アプリケーションプール・配置場所）に**変更はありませんでした**。

### (B) SmartClient から呼ばれるサーバー側のデバッグ

(A) と**まったく同じ手順**です。`w3wp.exe` にアタッチしたうえで、`PingController` などにブレークポイントを置き、SmartClient を起動すると停止します。

フェーズ3以降、`/PLM/api/sso/ticket` や `/PLM/api/token/exchange` の処理を追う際に、この方法が中心になります。

### (C) SmartClient 本体（クライアント側）のデバッグ

こちらはクライアントアプリなので、通常の F5 実行です。フェーズ1で確認したとおり、**コマンドライン引数で引換券を渡します**。

- プロパティ →「デバッグ」→「コマンドライン引数」に `--ticket=TESTTICKET`
- F5 で実行

ClickOnce 起動ではないため `ActivationUri` は使えませんが、コードが両対応になっているためそのまま動きます。

**(B) と (C) を同時に行う**こともできます。VS を2つ起動し、一方で `w3wp.exe` にアタッチ（サーバー側）、もう一方で SmartClient を F5 実行（クライアント側）とすれば、往復の両側を同時に追えます。

### 補足：`DevRemoteUser` について

`<ISAPI>` の対応表により **`localhost` 経由でも SP 保護が掛かる**ため、「SSO なしで画面だけ確認する入口」としては使えません。SSO を経由しない確認をしたい場合は、**`<ISAPI>` に登録されていない別ポートの IIS サイト**を新規作成して同じフォルダを指す方法があります（SP の処理対象外になるため）。当面は不要です。

---

## トラブルシューティング

| 症状 | 原因の候補 | 対処 |
|---|---|---|
| `/PLM/Default.aspx` が 500 エラー | ソースフォルダのアクセス権不足 | `IIS_IUSRS` に読み取りと実行を付与（手順3-c） |
| `Default.aspx` が 404 / ダウンロードされる | ASP.NET 4.8 未導入、アプリケーション未設定 | IIS の機能を確認し `iisreset` |
| `/PLM/api/ping` が「No type was found that matches the controller named 'ping'」 | **コントローラーがプロジェクトに含まれていない**（Explorer でコピーしただけ等） | ソリューションエクスプローラーの「すべてのファイルを表示」→ `Controllers\PingController.cs` を右クリック →「**プロジェクトに含める**」→ リビルド |
| `/PLM/api/ping` が 404（Web API のエラー JSON ですらない） | Web API の NuGet 未導入、ルーティング未登録 | `Global.asax.cs` の `Application_Start` を確認 |
| 画面の一部だけ文字化けする | `.aspx` の文字コード | 上記「2-2. 文字コードの注意」を参照 |
| `/PLM/api/ping` で SSO 画面に飛ぶ | 入れ子の `<Path>` が未反映 | `shibboleth2.xml` を確認し Daemon と IIS を再起動 |
| ClickOnce が起動しない／更新エラー | インストール URL の不一致 | `/PLM/smartclient/` で**再発行**（手順5） |
| `REMOTE_USER` が空（IIS 上で） | SP のセッションが無い | 画面の `Shib-Session-ID` が空でないか確認 |
| 「開発モード」警告が IIS 上で出る | `DevRemoteUser` が残っている | 設定を削除する |
| F5 で `Cannot create the Web site ... You must specify "localhost"` | VS の「ローカル IIS」はホスト名 localhost しか受け付けない | デバッグ実行の節「なぜ F5 が使えなかったか」参照。**アタッチ方式を使う** |
| F5 で `Unable to start debugging ... 0x80004005` | SP が VS のアタッチ用リクエストに介在している | 同上。**アタッチ方式を使う**（Windows 認証の有効化では解決しない） |
| ブレークポイントで止まらず `ERR_CONNECTION_REFUSED` | IIS Express で起動している（ツールバー表示を確認） | サーバーを「Local IIS」に変更、または**アタッチ方式を使う** |
| アタッチ時に「別のユーザーとして実行されているプロセス」警告 | `w3wp.exe` はアプリプール ID で動作 | **正常。そのまま進める** |
| ビルドエラー（名前空間） | プロジェクト名と名前空間の不一致 | `Inherits` を実際の名前空間に合わせる |

---

## このフェーズが通ったら次にやること

**フェーズ3：引換券の発行と JWT の交換**に進みます。ステップ6・7で検証した仕組みを、この土台の上に実装します。

- `Default.aspx` が **REMOTE_USER を使って引換券を取得**し、SmartClient の起動リンクに埋め込む
- `/PLM/api/sso/ticket`（引換券の発行）と `/PLM/api/token/exchange`（JWT への交換）を Web API 2 で実装
- SmartClient が引換券を JWT に交換し、`/PLM/api/whoami` を呼ぶ
- 署名鍵の **Windows 資格情報マネージャー**対応（フェーズ4。先に固定値で通してから移行するのが安全）

ここまでで、ブラウザで SSO 認証 → リンククリック → SmartClient 起動 → **パスワードなしで認証済み API 呼び出し**、という一連の流れが実環境で通ります。
