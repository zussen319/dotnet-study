# Shibboleth SSO 検証環境 構築手順書（純 Windows 版）

## 0. 改訂履歴

| 版 | 日付 | 変更概要 | 備考 |
|----|------|----------|------|
| 0.1 | 2026-07-05 | 純 Windows 版として新規作成。§1〜§4（目的・前提・全体アーキテクチャ・パラメータ・ロードマップ）を記載 | WSL 版 v0.15 を土台に、顧客 PLM 検証環境の再現用として起こす |
| 0.2 | 2026-07-05 | フェーズ1（検証環境の初期設定：前提確認・時刻同期・着手前チェックポイント）を追記。推奨アカウント値（わかりやすさ優先）を §3 に追記 | 入れ子仮想化は不要のため簡素化 |

> 本書は、WSL 版構築手順書（WSL2 上に IdP スタックを構築した学習環境）を土台に、**WSL を一切使わない純 Windows 構成**で顧客 PLM 検証環境を再現するための手順書です。SP 側（IIS＋Shibboleth SP）と SAML 設計の考え方は WSL 版から流用し、IdP 側（Tomcat／Shibboleth IdP）・LDAP を Windows ネイティブに置き換えています。

---

## 1. 目的と前提

- **目的**：顧客 PLM 環境（Shibboleth=SP、前段=IIS、PLM は `C:\inetpub\wwwroot` 配下の IIS アプリ、IdP=Entra ID／独自認証）に近い SAML SSO 検証環境を、**全メンバーが習熟している Windows のみ**で構築し、SP 側の設定と動作を確認する。
- **方針**：コンテナや WSL を使わず、**すべて Windows 上**で手作業構築する。IdP は学習用に自前構築し、将来 Entra ID に差し替え可能な形にする。
- **物理ホスト**：検証用PC（Windows 11、Hyper-V 有効）。
- **検証用 Windows**：Hyper-V 仮想マシン上の **Windows 11 Enterprise 評価版**（90日／`slmgr /rearm` で延長。付録A）。
- **役割分担（すべて同一のゲスト Windows 上に同居）**：
  - **IIS ＋ Shibboleth SP**：ブラウザからの入口（前段）。未認証を IdP へ、認証後は REMOTE_USER を確定し、`wwwroot` 配下の PLM 相当アプリを実行。
  - **Tomcat ＋ Shibboleth IdP**：学習用テスト IdP（HTTPS 8443 を直接公開）。将来 Entra ID に差し替え可能。
  - **ApacheDS（LDAP）**：ユーザーディレクトリ。IdP が認証・属性取得のために参照。
- **前提（電源管理）**：物理ホスト・ゲスト仮想マシンとも **Windows のスリープ／休止は無効化**していること（組織の対象PCも同様の運用）。本書はこの前提に立ち、スリープ復帰に伴う時刻ずれ対策は主手順から除外する（参考手順は付録C）。
- **顧客環境との対応（確認済み事項）**：
  - 前段＝**IIS**、SP＝**Shibboleth SP（IIS ネイティブモジュール）**、PLM＝**IIS 上のアプリ**（顧客と一致）。
  - IdP＝**Entra ID（＋独自認証）**。識別子は **emailAddress 形式**（例 `01PLM01@plm-lab.local`）。
  - PLM は将来、メール形式の識別子を受け取り `@` の前を切り出して従来の識別番号として扱う想定（**PLM アプリ側の責務**であり、本書の SP／IdP 構築範囲外）。

> ⚠️ **重要な前提**：本環境は Windows 仮想マシン1台にすべてが同居します。仮想マシンを削除すると IdP／LDAP／SP すべてが失われます。評価版の期限は削除・再インストールではなく `slmgr /rearm` で延長してください（付録A）。作業成果の保全・再構築検証の考え方は付録Bを参照。

---

## 2. 全体アーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│ 検証用PC：Windows 11 ＋ Hyper-V                                │
│                                                              │
│   ┌──────────────────────────────────────────────────────┐  │
│   │ Hyper-V ゲスト：Windows 11 Enterprise 評価版（1台に同居）│  │
│   │                                                      │  │
│   │   ┌──────────────────┐                                │  │
│   │   │ IIS + Shibboleth SP│  ホスト名: sp.plm-lab.local    │  │
│   │   │ (443/HTTPS)        │  entityID: https://sp.../shibboleth │
│   │   │ 保護対象=PLM相当     │                                │  │
│   │   └───────┬──────────┘                                │  │
│   │           │  SAML（ブラウザ経由のリダイレクト）           │  │
│   │           ▼                                            │  │
│   │   ┌──────────────────┐  ホスト名: idp.plm-lab.local     │  │
│   │   │ Tomcat + Shib IdP  │  (8443/HTTPS 直・Apache 不要)   │  │
│   │   │ (8443/HTTPS)       │  entityID: https://idp.../idp/shibboleth │
│   │   └───────┬──────────┘                                │  │
│   │           │  LDAP（389・サーバ間通信）                    │  │
│   │           ▼                                            │  │
│   │   ┌──────────────────┐                                │  │
│   │   │ ApacheDS (LDAP 389) │  baseDN: dc=plm-lab,dc=local   │  │
│   │   └──────────────────┘                                │  │
│   └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

**SAML Web SSO の流れ（未認証時）：**

1. ブラウザが IIS（SP・443）の保護対象（PLM 相当）にアクセスする。
2. SP が「未認証」と判断し、ブラウザを IdP（8443）へリダイレクトする。
3. ブラウザが IdP（Tomcat・8443）のログイン画面にアクセスする。
4. IdP が ApacheDS（LDAP・389）に問い合わせてユーザーを認証する（サーバ間通信）。
5. IdP が SAML アサーションを、ブラウザ経由で SP の ACS へ POST する。
6. SP がアサーションを検証してセッションを確立し、`REMOTE_USER` を確定する。
7. IIS が `wwwroot` 配下の PLM 相当アプリを実行して応答する。

> **WSL 版との違い**：IdP 側が Windows ネイティブのため、**IdP 前段の Apache HTTPD は不要**（Tomcat が HTTPS 8443 を直接公開）。WSL2 の mirrored ネットワーク・localhost 転送・ポート衝突といった問題は発生しない。IIS（SP）と Tomcat（IdP）は同一 Windows 上に同居するが、**互いに直接は通信せず**、ブラウザのリダイレクトと事前交換したメタデータで連携する。LDAP を参照するのは IdP のみ。

---

## 3. パラメータ一覧（確定・調整用）

> 本書全体で参照する値。**着手前に内容を確認**し、変更する場合はここを起点に各フェーズへ反映してください。

| 項目 | 既定値 | 備考 |
|------|--------|------|
| 内部ドメイン | `plm-lab.local` | 検証用の架空ドメイン |
| IdP ホスト名 | `idp.plm-lab.local` | Tomcat（IdP）側 |
| SP ホスト名 | `sp.plm-lab.local` | IIS（SP）側 |
| Java | OpenJDK 17（Windows 版） | Shibboleth IdP 5 の要件。Temurin 等 |
| Servlet コンテナ | Apache Tomcat 10.1.x（Windows サービス） | IdP 5 は Tomcat 10.1 必須（9系不可） |
| IdP | Shibboleth IdP 5.x | Java 17 / Jakarta 名前空間 |
| SP | Shibboleth SP 3.x（IIS ネイティブモジュール） | IIS 用 |
| LDAP | **ApacheDS**（Java 製・Windows サービス） | Apache Directory Studio で投入。`inetOrgPerson` |
| IdP entityID | `https://idp.plm-lab.local/idp/shibboleth` | フェーズ5で確定 |
| SP entityID | `https://sp.plm-lab.local/shibboleth` | フェーズ8で確定 |
| LDAP ベースDN | `dc=plm-lab,dc=local` | ApacheDS（フェーズ3） |
| LDAP 検索バインド | `uid=idp-reader,ou=people,dc=plm-lab,dc=local` | IdP の LDAP 検索用（読取専用） |
| テストユーザー | `uid=01PLM01` / `uid=01PLM02` | `ou=people` 配下。識別番号（社内テスト環境の形式に準拠） |
| テストユーザーの mail | `01PLM01@plm-lab.local` / `01PLM02@plm-lab.local` | **emailAddress 形式 NameID の元**（顧客 Entra の形式に準拠） |
| NameID 形式 | `emailAddress` | 顧客（Entra ID）の形式に合わせる（WSL 版の unspecified から変更） |
| REMOTE_USER | 優先リスト（例 `azure_id … gid_id` に倣う） | メール形式 NameID を載せる。学習では 1〜2 個で可 |
| IdP ホーム | `C:\opt\shibboleth-idp` | Shibboleth IdP 5（Windows・フェーズ5） |
| Tomcat ホーム | `C:\opt\tomcat`（例） | Windows サービスとして稼働（フェーズ4） |
| SP インストール先 | `C:\opt\shibboleth-sp` | 既定。`C:\inetpub` 配下は不可（鍵・設定の漏洩リスク） |
| PLM アプリ配置 | `C:\inetpub\wwwroot` 配下 | 検証では REMOTE_USER 表示のテストページ |
| SP ポート（HTTPS） | 443 | IIS（SP） |
| IdP ポート（HTTPS） | 8443 | Tomcat（IdP）直公開。同一 Windows 上での 443 衝突回避 |
| 内部CA ルート名 | `PLM-Lab Root CA` | フェーズ2で作成。idp/sp のサーバ証明書を発行 |
| 証明書作成ツール | openssl for Windows（Git for Windows 同梱等） | WSL 版の openssl 手順を流用 |
| ゲストVM メモリ | 8 GB（目安） | フルスタック同居のため |
| ゲストVM vCPU | 4 | 目安 |
| タイムゾーン | Asia/Tokyo | ゲスト Windows |

> ホスト名は当面 `hosts` ファイルで解決します（DNSサーバは立てません）。SP・IdP とも同一ゲスト Windows 上のため、`127.0.0.1` で解決させます（フェーズ2）。

> **2種類の証明書（再掲・重要）**：(a) TLS/HTTPS サーバ証明書（ブラウザに信頼される・SAN 一致必須。内部CAで idp/sp を発行）と、(b) SAML 署名・暗号化証明書（メタデータ交換で信頼・自己署名可・CA信頼もホスト名一致も不要。IdP はインストール時に自動生成、SP は keygen で生成）。準備が要るのは (a)。詳細はフェーズ2で扱う。

### 3.1 アカウント・パスワード一覧（わかりやすさ優先）

> 本検証環境では**わかりやすさを優先**し、既定値や「Joe アカウント（uid とパスワードが同じ）」を採用する。**いずれも検証専用であり、実際の環境構築時にはセキュリティを考慮して強固な値に変更すること。** OS ユーザーとしての専用アカウント（WSL 版の `tomcat` 相当）は Windows では作らず、各サービスは Windows のサービスアカウント（`Local System` 等）で動作する。構築作業はローカル管理者（本書では **`Administrator`**。読み替え可）で、昇格した PowerShell／コマンドプロンプトで行う。

| # | アカウント／鍵 | 値（検証用） | 使用箇所 | フェーズ |
|---|---------------|--------------|----------|---------|
| 1 | ApacheDS 管理者 | `uid=admin,ou=system` / `secret` | ApacheDS 管理（既定値のまま） | 3 |
| 2 | テストユーザー1 | uid=`01PLM01` / パスワード `01PLM01` | ログイン検証（Joe アカウント） | 3 |
| 3 | テストユーザー2 | uid=`01PLM02` / パスワード `01PLM02` | 2人目・再現性検証（Joe アカウント） | 3 |
| 4 | IdP 検索バインド | uid=`idp-reader` / パスワード `idp-reader` | IdP の LDAP 検索（読取専用） | 3・5 |
| 5 | IdP キーストア | `changeit` | IdP install.bat 対話 | 5 |
| 6 | IdP Sealer | `changeit` | IdP install.bat 対話 | 5 |
| 7 | TLS 証明書 PFX（idp/sp） | `changeit` | idp.pfx / sp.pfx の作成・取込（Tomcat・IIS） | 2・6・7 |

> **OS ユーザーの扱い**：構築は `Administrator`（ローカル管理者）。Tomcat/IdP・ApacheDS・shibd（Shibboleth Daemon）はいずれも Windows サービスとして `Local System` 等のサービスアカウントで動作するため、Linux の `tomcat` ユーザーのような専用 OS ユーザーの作成・所有権付与は不要。

---

## 4. 構築フェーズ全体像（ロードマップ）

| フェーズ | 内容 | WSL 版との関係 | 本書での状態 |
|---------|------|----------------|------------|
| **1** | 検証環境の初期設定（ゲスト Windows VM・時刻同期・前提） | 新規（WSL 導入を廃し簡素化） | ✅ 本版で記載 |
| 2 | ネットワーク土台（hosts・内部CA・idp/sp 証明書(a)・ポート設計） | 変更（openssl for Windows で流用） | ⬜ 未 |
| 3 | ApacheDS（LDAP）導入・ディレクトリ設計・テストユーザー投入 | 新規（OpenLDAP から置換） | ⬜ 未 |
| 4 | OpenJDK ＋ Tomcat（Windows サービス） | 変更（Windows 版・service.bat） | ⬜ 未 |
| 5 | Shibboleth IdP 5（Windows・LDAP連携・emailAddress NameID 準備） | 変更（install.bat・Windows パス） | ⬜ 未 |
| 6 | Tomcat 直 HTTPS（8443）公開 | 置換（Apache 前段を廃止） | ⬜ 未 |
| 7 | IIS（SP の保護対象サイト・443/TLS） | 流用（WSL 版とほぼ同一） | ⬜ 未 |
| 8 | Shibboleth SP（IIS ネイティブモジュール・サイト全体保護） | 流用（WSL 版とほぼ同一） | ⬜ 未 |
| 9 | メタデータ交換（IdP ↔ SP の相互信頼・初回 SSO） | 変更（:8443 補正が不要に） | ⬜ 未 |
| 10 | 属性連携（emailAddress 形式 NameID を REMOTE_USER に載せる） | 変更（unspecified→emailAddress） | ⬜ 未 |
| 11 | 結合テスト（ログイン・再現性・再起動堅牢性・ログ） | 流用／変更 | ⬜ 未 |

各フェーズは「目的 → 前提 → 手順 → 動作確認」の順で記載します。付録：A（評価版 rearm）／B（バックアップ・再現性検証のチェックポイント運用）／C（スリープ環境向け時刻再同期）／D（トラブルシュート・Windows 固有）／E（オフライン導入）。

---

## 5. フェーズ1：検証環境の初期設定

**目的**：Hyper-V ゲストの Windows 11 を、以降の全スタック（ApacheDS／Tomcat＋IdP／IIS＋SP）を同居させる土台として整える。あわせて、SAML でつまずきやすい**時刻同期**を最初に固め、やり直しに備えた**着手前チェックポイント**を取得する。

> **WSL 版との違い**：本構成は WSL2 を使わないため、**入れ子（ネステッド）仮想化の有効化は不要**。ゲスト Windows 上で Java/Tomcat/IIS が動くだけなので、通常の Hyper-V ゲストとして扱える（WSL 版 §5.2 の `ExposeVirtualizationExtensions` や MAC スプーフィングは本版では不要）。

### 5.1 事前確認

- 検証用PCで Hyper-V が有効で、ゲストの Windows 11 評価版が作成済みであること。
- ゲストに割り当てるメモリ（8GB目安）・vCPU（4目安）の余裕が検証用PCにあること。
- ゲスト Windows に**ローカル管理者（`Administrator`。自宅では読み替え）**でサインインできること。
- 物理ホスト・ゲストとも **Windows のスリープ／休止が無効**であること（§1 の前提）。

### 5.2 ゲストVMの電源オプションと着手前チェックポイント

やり直しに備え、**素の状態のチェックポイント**を取得します。まず、電源断時に保存状態へ入らずシャットダウンさせる設定にしてから、VMを停止した状態でチェックポイントを取得します。検証用PC（物理ホスト）の**管理者権限 PowerShell**で実行します。

```powershell
# 1) 対象VM名を確認
Get-VM

# 2) 電源断時に保存状態にせずシャットダウンさせる（保存状態からの復帰による時刻ずれ回避）
Set-VM -Name "<VM名>" -AutomaticStopAction ShutDown

# 3) 種類を標準（Standard）チェックポイントに（本構成は入れ子仮想化を使わないため Standard で可）
Set-VM -Name "<VM名>" -CheckpointType Standard

# 4) ゲストをシャットダウン（停止状態で取得するのが最も安全）
Stop-VM -Name "<VM名>"

# 5) 着手前の素の状態を取得
Checkpoint-VM -Name "<VM名>" -SnapshotName "Phase1前_素のWindows11"
```

> ⚠️ **パラメータ名の注意（`-Name` と `-VMName`）**：VM 自体を操作する系（`Set-VM`／`Get-VM`／`Start-VM`／`Stop-VM`／`Checkpoint-VM`）は VM 名を **`-Name`** で指定する。VM の構成要素を操作する系（`Set-VMProcessor`／`Set-VMMemory` 等）は **`-VMName`** だが、本フェーズでは使用しない。取り違えを避けたい場合は `Get-VM "<VM名>" | Set-VM -CheckpointType Standard` のようにパイプで渡す。
>
> チェックポイントは短期のやり直し用でありバックアップではありません（付録B）。本構成は入れ子仮想化を使わないため、標準（Standard）チェックポイントが利用でき、稼働中の取得も比較的安全ですが、着手前の素の状態は**停止して**取得するのが確実です。

### 5.3 時刻同期の確認

SAML は IdP と SP の時刻の一致に敏感で、ズレると「clock skew」エラーの原因になります。本構成では IdP（Tomcat）も SP（IIS）も**同一のゲスト Windows 上**にあるため、両者の時刻は常に一致し、WSL 版のような OS 間の相対ずれは原理的に発生しません。したがって、ゲスト Windows の時計が正しく保たれていれば十分です。

- **Hyper-V 統合サービス「時刻同期」が有効**であることを確認（既定で有効）。これによりゲスト Windows は検証用PC（ホスト）の時計に追随します。ゲストの管理者 PowerShell で確認：

```powershell
# タイムゾーンが東京か
Get-TimeZone
# 東京でなければ設定
Set-TimeZone -Name "Tokyo Standard Time"

# Windows Time サービスが稼働しているか（オンライン時は NTP 同期も有効）
w32tm /query /status
```

> オンライン環境なら Windows Time サービス（w32tm）が NTP に同期します。オフライン環境でも、IdP と SP が同一 OS 上のため相対ずれは発生せず、SAML の clock skew は問題になりません。スリープを使う環境で運用する場合の復帰時再同期は付録Cを参照。

### 5.4 作業用フォルダの準備（任意）

以降のフェーズで、インストーラや証明書を扱う作業用フォルダを用意しておくと整理しやすいです（例）。

```powershell
New-Item -ItemType Directory -Force -Path C:\opt, C:\lab, C:\lab\ca, C:\lab\installers | Out-Null
```

- `C:\opt`：Tomcat／Shibboleth IdP／SP の導入先（各フェーズで使用）。
- `C:\lab\ca`：内部CA・証明書の作成場所（フェーズ2）。
- `C:\lab\installers`：入手した各インストーラの保管（ApacheDS／Tomcat／IdP／SP など）。

### 5.5 動作確認チェックリスト（フェーズ1）

| # | 確認内容 | コマンド / 方法 | 期待結果 |
|---|----------|----------------|----------|
| 1 | ゲストにローカル管理者でサインイン | — | `Administrator`（読み替え可）でサインイン済み |
| 2 | タイムゾーン | `Get-TimeZone` | Tokyo Standard Time |
| 3 | 時刻サービス | `w32tm /query /status` | 稼働（オンライン時は NTP 同期） |
| 4 | 着手前チェックポイント | Hyper-V マネージャー | `Phase1前_素のWindows11` が存在 |
| 5 | 昇格実行の確認 | 管理者 PowerShell が使える | 昇格プロセスでコマンド実行可 |

すべて確認できれば、フェーズ1は完了です。次はフェーズ2（ネットワーク土台・内部CA と idp/sp 証明書の発行）です。

---

> 以降のフェーズ（§6 フェーズ2〜§15 フェーズ11、および付録）は、フェーズごとに実機検証しながら順次追記します。
