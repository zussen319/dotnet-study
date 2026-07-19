# フェーズ1：ClickOnce 引数受け取りスパイク

実装フェーズの**最初の作業**です。目的をひとつに絞ります。

> **ブラウザのリンクから ClickOnce アプリを起動したとき、URL に付けた `?ticket=XXXX` がアプリに届くか。**

ここが成立するかどうかで、以降の設計（SSO からの引換券の渡し方）が変わります。SSO も JWT もまだ扱いません。**最小のアプリで、この一点だけを先に確定させます。**

> ⚠️ **本フェーズは提案版です。** 実行して判明した点は、これまで同様この冒頭に追記して確定版にしてください。

---

## 背景：なぜ検証が必要か

ステップ6では「引換券を SmartClient の起動パラメータに埋める」と設計しました。ClickOnce でこれを行う方法は、.NET のバージョンで大きく変わっています。

- **.NET Framework 時代**：`System.Deployment.Application.ApplicationDeployment.CurrentDeployment.ActivationUri` で取得。
- **.NET Core 3.1／.NET 5／.NET 6**：`ApplicationDeployment` クラスにプログラムからアクセスできず、この方法が使えない。
- **.NET 7 以降＋VS 2022 17.4 以降**：ClickOnce ランチャーが**環境変数**で起動情報を渡すようになった。`ClickOnce_IsNetworkDeployed`、`ClickOnce_ActivationUri` などが使える。

**今回は .NET 10 なので、環境変数方式が使えるはず**です。この「はず」を実機で確かめるのが本フェーズです。

成立には条件があります。

1. 配置マニフェストの **`TrustUrlParameters` が true** であること。false だと `ActivationUri` は空文字列を返します。
2. **HTTP(S) 経由での起動**であること。ファイル共有やローカルファイルからの起動ではクエリ文字列を渡せません。
3. **オンライン専用**にすること。インストール型だと、利用者は初回だけ URL から起動し、以降はスタートメニューのショートカットから起動するため、クエリ文字列を受け取れるのは実質一度きりになります。オンライン専用なら常に URL 経由で起動されます。**毎回 SSO 経由で引換券を受け取る今回の設計には、オンライン専用が合致します。**

---

## 成果物

| ファイル | 内容 |
|---|---|
| `Program.cs` | 起動情報を表示するだけの WinForms アプリ |
| `SpikeClickOnce.csproj` | net10.0-windows / WinForms |
| `launch-test.html` | 起動リンクを並べたテストページ |

---

## 手順

### 1. プロジェクトを作る

Visual Studio で「**Windows フォーム アプリ**」（C#）を新規作成し、名前を `SpikeClickOnce` にします。生成された `Form1.cs`・`Form1.Designer.cs`・`Program.cs` を削除し、本フォルダの `Program.cs` と `SpikeClickOnce.csproj` で置き換えます（フォームはコードで組み立てるため、デザイナーファイルは不要です）。

F5 で実行し、ウィンドウが開いて「ClickOnce 起動ではありません」と表示されれば準備完了です。

### 2. デバッグ実行で引数の受け取りを確認する

プロジェクトのプロパティ →「デバッグ」→「コマンドライン引数」に次を入力します。

```
--ticket=TESTTICKET
```

F5 で実行し、`ticket = TESTTICKET`（コマンドライン引数）と表示されることを確認します。**これは開発時の動かし方**でもあります（VS では ClickOnce 起動ではないため、環境変数は使えません）。

### 3. ClickOnce として発行する

プロジェクトを右クリック →「**発行**」→ ターゲットに「**ClickOnce**」を選びます。

設定の要点は次の3つです。

| 項目 | 設定値 | 理由 |
|---|---|---|
| 発行場所 | ローカルフォルダ（例：`C:\publish\smartclient`） | 後で IIS に配置 |
| インストールモード | **オンラインのみ**（インストールしない） | 毎回 URL 経由で起動させるため |
| インストール URL | `https://plmdev.plm-lab.local/smartclient/` | 実際に配布する URL |

**そして最重要の設定**が「**URL パラメーターの引き渡しを許可する**」です。発行ウィザードの「オプション」→「マニフェスト」にチェックボックスがあります。見当たらない場合は、発行プロファイル（`Properties\PublishProfiles\*.pubxml`）を直接編集してください。

```xml
<PropertyGroup>
  <TrustUrlParameters>true</TrustUrlParameters>
</PropertyGroup>
```

発行すると、`SpikeClickOnce.application`（配置マニフェスト）と `Application Files\` フォルダが生成されます。

### 4. IIS に配置する

発行フォルダの中身と `launch-test.html` を、IIS サイトの `/smartclient/` に配置します。

**IIS の MIME タイプ設定が必要です。** 既定では `.application` や `.manifest` が未登録で、そのままだと 404 になります。IIS マネージャーの「MIME の種類」で次を追加してください（既にあれば不要）。

| 拡張子 | MIME タイプ |
|---|---|
| `.application` | `application/x-ms-application` |
| `.manifest` | `application/manifest` |
| `.deploy` | `application/octet-stream` |
| `.msu` | `application/octet-stream` |

> **この段階では Shibboleth SP の保護は掛けません。** まず素の状態で通してから、フェーズ2で保護範囲を設計します。

### 5. ブラウザから起動して確認する

```
https://plmdev.plm-lab.local/smartclient/launch-test.html
```

を開き、(1)〜(3) のリンクを順に試します。

---

## 判定基準

| 確認項目 | 期待する結果 |
|---|---|
| (1) 固定の引換券 | アプリが起動し `ticket = TESTTICKET-12345` と表示される |
| (2) ランダムな引換券 | **毎回異なる値**が表示される（キャッシュされない） |
| (3) 引換券なし | アプリが起動し「受け取れませんでした」と表示（**落ちない**） |

**(2) が特に重要**です。本番では引換券は毎回変わるため、初回の値がキャッシュされて使い回されると設計が成立しません。

アプリ画面の「診断」欄に、失敗時の手がかりが出るようにしてあります。

---

## うまくいかない場合

### 症状別の切り分け

| 症状 | 原因の候補 | 対処 |
|---|---|---|
| リンクが 404 | MIME タイプ未登録 | 上記の MIME 設定を追加 |
| アプリは起動するが ticket が空、`ActivationUri` も空 | `TrustUrlParameters` が false | `.pubxml` を確認して再発行 |
| `ActivationUri` にクエリが無い | ブラウザが「ダウンロードしてから実行」している | 下記の代替案へ |
| そもそも起動せずファイルが保存される | ブラウザの ClickOnce 対応 | Edge の ClickOnce 設定を確認、または代替案へ |
| 起動時にランタイムが無いと言われる | .NET デスクトップランタイム未導入 | クライアントに導入するか、`SelfContained` で発行 |
| 発行元が不明という警告 | 署名証明書が仮のもの | 検証段階では続行して可（フェーズ2で内部CA署名を検討） |

### 代替案（ClickOnce でパラメータが渡せなかった場合）

設計を変える必要が出ますが、いずれも実績のある手法です。**フェーズ1で判明すれば手戻りは最小**で済みます。

- **代替案A：カスタム URI スキーム**
  アプリが `plmclient://` をレジストリに登録し、Web ページから `plmclient://ticket=XXXX` へ遷移させる。ClickOnce のパラメータ制約を完全に回避でき、アプリ側の実装も単純です。ただしアプリが一度インストールされている必要があります（＝オンライン専用にはできない）。

- **代替案B：ループバック方式**
  SmartClient がローカルポートで待ち受け、ブラウザが `http://127.0.0.1:PORT/?ticket=XXXX` にリダイレクトして引換券を渡す。OAuth 2.0 のネイティブアプリ標準（RFC 8252）と同じ仕組みで、**最も本番向きで堅牢**です。実装量は少し増えます。

- **代替案C：引換券を含む小さな設定ファイルを配布**
  `.plmlaunch` のような独自拡張子のファイルをブラウザからダウンロードさせ、ファイル関連付けで SmartClient を起動する。設定はやや煩雑です。

どれを採るかは、フェーズ1の結果を見てからご相談させてください。

---

## このフェーズで確認できたら次にやること

**フェーズ2：IIS のサイト構成整備**に進みます。

- HTTPS サイト（`plmdev.plm-lab.local`）の下に、`/`（.aspx）・`/api`（ASP.NET Core）・`/smartclient`（ClickOnce）を配置
- **Shibboleth SP の保護をパス単位で分ける**：`/` は保護、`/api` と `/smartclient` は保護から外す
- ASP.NET Core 側から `REMOTE_USER` が読めるか（`GetServerVariable`）の確認
- ASP.NET Core Hosting Bundle の導入確認

保護範囲の設計が、この構成全体の土台になります。
