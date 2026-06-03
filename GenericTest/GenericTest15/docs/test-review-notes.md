<div lang="ja"></div>
<style>body{font-family:"Yu Gothic UI","Meiryo","BIZ UDGothic",sans-serif;}</style>

# ServiceApi テストコード レビュー記録

- **対象**: `GenericTest15/tests/ServiceApi.Tests` 配下（API側のテストコード）
- **実装対象（参考）**: `GenericTest15/src/ServiceApi`（Service / Request / Response 構成、Oracle 接続 SELECT）
- **環境**: .NET 10 / C# 最新, xUnit, Oracle.ManagedDataAccess.Core
- **初回レビュー実施日**: 2026-05-31
- **最終更新日**: 2026-06-03

> このファイルは実装側 `review-notes.md` と対をなす「テスト指摘事項の台帳」です。各項目の `状態` を更新しながら使ってください。
> レビューは API側のクラス構成に従って進める。**第1弾は `ApiExecutor` クラスのテスト**（本ファイルの対象）。
> 方針メモ：Service の単体テストは現状 B1Service のみ厳密に用意し、A1/C1/C2 はサンプル段階のため簡易で可。

## 状態の凡例

- ✅ **実装反映済み** … テストコードに反映しビルド/実行確認まで完了
- ✅ **対応方針確定** … 修正案まで合意済み（実装するだけ）
- 💬 **議論済み** … 方針は話したが実装は未確定／要判断
- ⬜ **未着手** … これから議論（提示のみ）

---

## 対象の前提：ApiExecutor.RunAsync の観測可能な振る舞い

テストでカバーすべき対象を整理（指摘の根拠）。

- **入口ガード**: `requests` が null / 空 → `yield break`（サービス生成もログも無し）
- **生成**: `Activator.CreateInstance(typeof(TService), connectionString, fetchRows)`
- **破棄**: `await using (service)` により正常・異常・キャンセルいずれでも `DisposeAsync`
- **ログ分岐**: 開始 MSG001 / 正常終了 MSG002 / 異常終了 MSG003 / キャンセル MSG005 /
  `[Database Error]`（OracleException）vs `[System Error]`（その他）
- **例外伝播**: OperationCanceledException と一般例外を捕捉・ログ後に再スロー
- **引数順**: `ct` が `fetchRows` より前（実装側 review-notes §7 の設計判断）

---

## 総評（良い点）

- 正常終了・例外発生時の Dispose 確認、リクエスト0件時の「非インスタンス化」確認、
  件数の境界値（0/1/100/1000）など、ライフサイクルの肝は押さえている。
- `OracleException` を内部コンストラクタ経由のリフレクションで生成する方針は妥当
  （Activator 生成設計に合致。コメントの背景説明も的確）。
- DB停止確認テスト `_02` を `[Fact(Skip=...)]` で手動専用と明示しているのは良い運用。

---

## 指摘事項一覧（第1弾：ApiExecutor）

| # | 区分 | 概要 | 状態 |
|---|------|------|------|
| T1 | 高 | OracleException テストのアサートが弱く誤った理由でパスしうる | ✅ 実装反映済み（2026-05-31） |
| T2 | 中〜高 | キャンセル/タイムアウト時の破棄がコメントの主張に反して未検証 | ✅ 実装反映済み（2026-05-31） |
| T3 | 中 | `fetchRows` 引き渡し と ct→fetchRows 引数順（src#7）の回帰テスト無し | ✅ 実装反映済み（2026-06-02） |
| T4 | 中 | ライブDB依存の結合テストが単体と同居・無分類・アサート弱 | ✅ 対応（現状維持＋整理 2026-06-03） |
| T5 | 中 | ログ分岐（MSG002/003/005, DB/Systemエラー文言）が未検証 | ⬜ 未着手 |
| T6 | 中 | コメントと実装の不一致（旧DI/scope・存在しないスタブ名） | 🟡 一部対応（キャンセル確認_01 の3件 2026-05-31） |
| T7 | 低〜中 | `requests is null` 経路が未テスト | ⬜ 未着手 |
| T8 | 低 | スタブのエラー注入パターンが不統一 | ⬜ 未着手 |
| T9 | 低 | 静的可変状態の順序/並列依存 | ⬜ 未着手 |
| T10 | 低 | アサートが薄い（件数/NotEmpty のみ） | ⬜ 未着手 |
| T11 | 低 | スタブ専用テストが実DB接続文字列(config)に依存 | ⬜ 未着手 |
| T12 | スタイル | 例外伝播テストの重複（throwタイミング差のみ） | ⬜ 未着手 |

---

## T1. OracleExceptionハンドリングのアサートが弱い ✅ 実装反映済み（2026-05-31）【高】

### 問題
`RunAsync_異常系_OracleExceptionハンドリング確認_01`（`TEST_ApiExecutor.cs` L648 付近）が
`Assert.ThrowsAnyAsync<Exception>` のみで検証していた。これだと、`OracleErrorStub.CreateOracleException`
のリフレクションが失敗して別の例外が飛んだ場合でも**テストがパス**してしまい、
「`catch (OracleException)` 分岐を実際に通ったか」を検証できない。
テスト名（OracleExceptionハンドリング確認）の主張と乖離していた。

### 採用した対応
- `Assert.ThrowsAsync<OracleException>` に変更し、`ex.Number == 12154`（生成時に指定したコード）まで確認。
- これにより「リフレクション生成が成功し、かつ Oracle 専用分岐を通った」ことが保証される。

### このテスト強化で表面化した重大バグ（重要）
アサートを `OracleException` 限定に強化したところ、`TargetParameterCountException`（Parameter count mismatch）が
発生し、**従来のテストは「Oracle 専用分岐を通ったから」ではなく「スタブのリフレクションが例外を投げていたから」
パスしていた**ことが判明した（＝従来テストは実質無意味だった）。T1 の狙いどおりの収穫。

原因と修正：
- `OracleErrorStub.CreateOracleException` が `GetConstructors(...).FirstOrDefault()` で
  先頭の内部コンストラクタ（`OracleErrorCollection` 1引数版）を無条件に掴み、そこへ `[message, errorCode]`（2個）を
  渡していたため引数個数が不一致だった。
- net10 実機で `OracleException` の内部コンストラクタを確認し、5引数版
  `(int errCode, string dataSrc, string procedure, string errMsg, int parseErrorOffset)` を
  `GetConstructor(types: ...)` で型指定して取得、`(errorCode, null, null, message, 0)` を渡す実装に修正。
  → `ex.Number == 12154` が正しく入ることを実機確認済み（`Message` には Oracle のヘルプURLが付与されるため
     等価比較せず `Number` のみ検証する設計が妥当）。
- あわせて事実誤認のコメントを訂正：`OracleException` は **`sealed = True`**（テスト内コメントの
  「sealed ではないものの」は誤り）。

### 残置メモ
- 修正前/修正後の `CreateOracleException` は `#if true / #else / #endif` で括り、後日確認できるよう一時残置
  （実装側 review-notes §9 と同方針）。不要と判断した時点で担当者が削除する。
- 手動反映時に、クラス閉じ `}` が `#else` ブロック内に入り `OracleErrorStub` が閉じず、後続テストメソッドが
  入れ子化して CS0120（`_connectionString` にインスタンス参照が必要）が発生 → クラス閉じ `}` を `#endif` の
  外へ移して解消（`#if/#else/#endif` で構造ブロックを括る際の定番の注意点）。

### 調査ログ：OracleException の内部コンストラクタ特定（2026-05-31）

リフレクション対象の内部コンストラクタ署名を実機で確定するまでの手順。

**結論（先に）**
- `OracleException`（ODP.NET Managed **23.26.200**）は **`sealed`**、コンストラクタは全て `internal`。
- 2引数のコンストラクタは存在しない（旧スタブが `[message, errorCode]` の2個を渡していたのが
  `TargetParameterCountException` の原因）。
- 利用すべきは5引数版 `(int errCode, string dataSrc, string procedure, string errMsg, int parseErrorOffset)`。
  `(errorCode, null, null, message, 0)` で生成すると `Number` が errCode になる。
- `Message` には Oracle のヘルプURL（`https://docs.oracle.com/error-help/db/ora-12154/`）が自動付与されるため、
  メッセージの等価比較は避け **`Number` のみ検証**する。

**手順と要点**
1. **DLL の場所を特定**（NuGet パッケージ + プロジェクト bin を再帰検索）。
   - パッケージ：`%USERPROFILE%\.nuget\packages\oracle.manageddataaccess.core\23.26.200\lib\net8.0\Oracle.ManagedDataAccess.dll`
   - テスト bin：`tests\ServiceApi.Tests\bin\Debug\net10.0-windows\Oracle.ManagedDataAccess.dll`
2. **PowerShell で直接リフレクション → 失敗**。`Assembly.LoadFrom` 後 `GetType(...)` が null、`GetTypes()` も 0 件。
   依存解決に失敗し型をロードできず。
3. **`MetadataLoadContext` で読もうとする → 失敗**。Windows PowerShell 5.1（.NET Framework）には
   当該アセンブリが存在せず利用不可。
   → 教訓：**PowerShell 5.1 では net8/net10 アセンブリのリフレクション調査は不向き**。
4. **net10 の使い捨てコンソールで調査 → 成功**。`C:\temp\oraprobe` に net10 コンソールを作成し、
   DLL を `<Reference HintPath=...>` でパス参照（NuGet 復元不要）。`GetConstructors(NonPublic|Public|Instance)` を
   ダンプして上記4種の署名を確認。
   ```
   Sealed: True
   [internal] (OracleErrorCollection oec)
   [internal] (NetworkException inner)
   [internal] (Int32 errCode, String dataSrc, String procedure, String errMsg, Int32 parseErrorOffset)
   [internal] (Int32 errCode, String dataSrc, String procedure, String errMsg, Exception innerException)
   ```
5. **実生成して検証 → 成功**。5引数版を `GetConstructor(types: ...)` で取得し
   `Invoke([12154, null, null, "TNS:...", 0])`。`Number = 12154`、`Message` 末尾にヘルプURL付与を確認。
6. **後片付け**：調査用一時プロジェクト `C:\temp\oraprobe` は削除済み（リポジトリには残さない）。

**学び**
- 内部コンストラクタをリフレクションで叩くテストは、**型シグネチャを実機で確定してから**書く。
  でないと今回のように静かに別の例外で誤動作し、テストが「誤った理由でパス」する。
- 調査は **ターゲットフレームワークを合わせた使い捨てコンソール + DLL パス参照**が確実で速い。

---

## T2. キャンセル/タイムアウト時の破棄がコメントの主張に反して未検証 ✅ 実装反映済み（2026-05-31）【中〜高】

### 問題
`RunAsync_異常系_ApiExecutor実行キャンセル確認_01` / `_タイムアウトキャンセル確認_01` のコメント
（L460-468 / L516-525）は「リソースの即時解放が保証される／検証できる」と書いているが、
実テストは `OperationCanceledException` のスローのみをアサートし、`DisposeAsync` 呼び出しは確認していなかった。
`ServiceCancelStub` / `ServiceTimeoutStub` は破棄追跡フラグも持たなかった。
→ コメントとテスト内容のミスマッチ（実装側 review-notes が重視する「コメントと実装の不一致」と同種）。

### 採用した対応
- `ServiceCancelStub` / `ServiceTimeoutStub` に破棄追跡フラグ `static bool IsDisposed` を追加し、
  `DisposeAsync` を override して `true` をセット。
- 各テストの Arrange で `IsDisposed = false` にリセットし、例外スロー後に
  `Assert.True(...IsDisposed)` を追加。
- ApiExecutor の `await using (service)` が例外（キャンセル含む）でブロックを抜ける瞬間に `DisposeAsync` を
  呼ぶため、両テストとも緑化を確認（＝コメントが主張していた「即時解放」を実証）。

### 補足
- 今回は既存ロジックの置き換えではなく「破棄追跡フラグと assert の追加」だが、担当者の意向で変化点を後日
  確認できるよう `#if true / #else / #endif` で残置（T1 と同方針）。
- コメント中の「`ApiExecutor` 内の `using var scope`」表現は現行実装（`await using (service)`）と不一致のまま
  据え置き。これは **T6（コメントと実装の不一致）** でまとめて正確化する。

---

## T3. fetchRows 引き渡し と ct→fetchRows 引数順（src#7）の回帰テスト無し ✅ 実装反映済み（2026-06-02）【中】

### 問題
ApiExecutor は `fetchRows` を `Activator.CreateInstance` の第2引数として渡すが、全スタブが受け取って
無視するため、**既定値100が渡るか／明示値が伝わるか**が一切検証されていなかった。
実装側 review-notes §7 で「`ct` を `fetchRows` より前に置く」と意図的に決めた引数順を守るガードも無かった。

### 採用した対応
- コンストラクタで受け取った `fetchRows` を `static CapturedFetchRows` に記録するスタブ
  `FetchRowsCaptureStub`（`TestServiceBase` 継承）を追加。
- 以下3テストを追加し、いずれも緑化を確認：
  - (a) `RunAsync_正常系_fetchRows既定値がサービスに渡る_01` … fetchRows 省略時に
    `ApiConstants.DefaultFetchRows`(100) が渡る。
  - (b) `RunAsync_正常系_fetchRows明示指定がサービスに渡る_01` … `fetchRows: 500` が伝播する。
  - (c) `RunAsync_正常系_ct位置指定でfetchRowsは既定のまま_01` … `ct` を第3引数（位置指定）で渡しても
    fetchRows は既定のまま。§7 の引数順（ct を前）に対する回帰ガード
    （順序が逆なら CancellationToken を int 引数に渡せずコンパイルエラーになる）。
- サービスは最初の `MoveNextAsync`（`await foreach` 開始）時に `Activator.CreateInstance` で生成されるため、
  ストリームを列挙すればコンストラクタが呼ばれ fetchRows を捕捉できる。

### 補足
- `ApiConstants` 参照のため `using ServiceApi.Common;` を追加（既定値はリテラル直書きせず
  `ApiConstants.DefaultFetchRows` 参照とし、既定値変更時も回帰検知できるようにした）。
- 純粋な追加のため `#if true/#else/#endif` 残置は不要（修正前が存在しない）。
- (a) と (c) はアサート結果が同じ（既定100）だが狙いが異なる（(a)=素の既定値、(c)=ct位置指定で
  fetchRows を巻き込まない＝§7 の本質確認）ため両方残置。
- 静的状態（`CapturedFetchRows`）は各テスト Arrange でリセット。T9（静的状態の脆さ）と同構図だが
  クラス内直列実行のため現状は問題なし。

---

## T4. ライブDB依存の結合テストが単体と同居・無分類・アサート弱 ✅ 対応（現状維持＋整理 2026-06-03）【中】

### 問題（当初）
`〜Service実行_01`（A1/B1/C1/C2 実DB）と `〜Service_Test実行_01`（JSON読込＋`Task.Delay`）が、
純粋なスタブ単体テストと同じクラスにあり、`[Trait]`/Skip 等の区別なく、`Assert.NotEmpty` のみ。
DBやJSONが無い環境では失敗・遅延し、テストスイートが hermetic でない。

### 確定した方針（担当者判断）
**「DBサービス起動」を前提条件としてよい**ため、A1/B1/C1/C2 の実サービス（`XXService`）と
テスト用サービス（`XXService_Test`）の**最小限の稼働確認（スモーク）を簡易に実行できる状態で残す**。
複雑化を避けることを優先し、当初案は以下のとおり**いずれも見送り**とした。

- 分離/分類（`[Trait]`/別コレクション/別プロジェクト）… 見送り（DB起動前提でよく、複雑化を避ける）
- アサート強化（件数・値の突合）… 見送り（`NotEmpty`＝最小稼働確認のまま。正しさは `TEST_B1Service` が担当）
- A1/C1/C2 の実行テスト削除 … 見送り（4サービスのスモークを残したいというご意向）
- 配置（`TEST_ApiExecutor` のまま）… 現状維持（強いこだわりなし）

### 実施した軽微整理（2026-06-03）
1. **csproj の dangling 指定を削除**：`<None Update="Services\B1\B1Service_Test.json">` は実体ファイルが
   存在せず（`Services/` 配下に json なし）、空フォルダ `bin/Services/B1` を作るだけの no-op だった。
   実際に読まれる `B1Service_Test.json` はルートの `<None Update="B1Service_Test.json">` が bin 直下へ供給
   （こちらは残置）。削除してもテスト動作に影響なしを実ファイルで確認済み。
2. **前提コメント追記**：スモーク群の先頭（`A1Service実行_01` 直前）に「実DBサービス起動前提・
   `XXService`=実DB / `XXService_Test`=スタブ・正しさは TEST_B1Service 担当・DB未起動時の失敗は想定挙動」を明記。

### 誤指摘の訂正（重要）
レビュー初回に「`A1Service_Test.json` 不在で `RunAsync_正常系_A1Service_Test実行_01` が壊れている」と
指摘したが、これは**誤り**。`A1Service_Test` は `ExecuteAsync` を override してデータをコード生成しており
（[A1Service_Test.cs](../src/ServiceApi/Services/A1/A1Service_Test.cs)）、JSON を読まないため `A1Service_Test.json` は不要。
B1/C1/C2 の `_Test` は基底 `TestServiceBase` の JSON 読込方式（各 `*Service_Test.json` 必要・存在・コピー済み）。

### 残課題（任意・将来）
- 将来「本番想定」フェーズで結合テストを増やす場合は、その時点で `[Trait]` 分類や別プロジェクト化を再検討する。

---

## T5. ログ分岐（MSG002/003/005, DB/Systemエラー文言）が未検証 ⬜ 未着手 【中】

### 問題
正常終了 MSG002・異常終了 MSG003 の出し分け、キャンセル MSG005、`[Database Error]`/`[System Error]`
の文言分岐は ApiExecutor の重要な観測可能挙動だが、`Console.WriteLine` 直書き
（実装側 review-notes §3 でログ抽象化を見送り）のため一切アサートされていない。

### 対応案
- 当面は「既知の制約」として記録（src §3 のログ方針確定とセットで再検討するのが自然）。
- 最低限、正常/異常終了の出し分けだけでも `Console.SetOut` で出力を捕捉して検証する手はある。

---

## T6. コメントと実装の不一致 🟡 一部対応（2026-05-31）【中】

### 問題
現行 ApiExecutor は DI/scope を使わず `await using (service)` + `Activator` だが、テストコメントが
旧DI設計のまま残っている（チームが特に重視する「コメントと実装の不一致」）。
- L41-43：「DIスコープの連動」「`using var scope` が正常に機能し」
- L466：「`using var scope` は…例外が発生してメソッドを抜ける瞬間に実行」
- L476 / L485：実在しない `B1Service_CancelStub` / 「B1Service 側のループ」
  （実体は `ServiceCancelStub` / `MockRequest`）

### 対応案
- 上記コメントを現行実装（`await using (service)` / Activator / 実際のスタブ名）に合わせて修正。

### 先行対応済み（2026-05-31）：`RunAsync_異常系_ApiExecutor実行キャンセル確認_01` の3件
T2 対応の流れで、当該テストの解説コメントを実装と突き合わせて以下を訂正済み。
1. 「`using var scope`」→ `await using (service)`（DisposeAsync を呼ぶ主体を正しく記述。
   あわせて本番では DB セッション解放、本テストでは `ServiceCancelStub.IsDisposed` で破棄検証、を明記）。
2. 実在しない「`B1Service_CancelStub`」→ 実体の `ServiceCancelStub`。
3. 「`B1Service` 側のループ」→ サービス（`ServiceCancelStub`）側のループ。

補足：
- タイムアウト確認_01 のコメントは「`await using`」と正しく記述されており、T2 の破棄アサート追加で
  「破棄を検証する」記述も実裏付けが取れた状態（訂正不要）。
- `cts.Token` を第3引数で渡しつつ `stream.WithCancellation(cts.Token)` も付けているのは機能的に冗長
  （二重指定）だが害はなく、コメントの「残して問題ない」は妥当。整理は任意。

### 残り（未対応）
- L41-43（クラス先頭の解説コメント）の「DIスコープの連動」「`using var scope`」。
- その他テスト全体に残る旧DI/scope 由来の表現の総点検。
  → ApiExecutor 分の他テストを一通り見た後、まとめて正確化する。

---

## T7. requests is null 経路が未テスト ⬜ 未着手 【低〜中】

### 問題
ApiExecutor は `requests is null` を明示的にガードしているが、`RunAsync_正常系_リクエスト件数0件の検証_01`
は `Enumerable.Empty<MockRequest>()` のみで null ケースが無い。

### 対応案
- null を渡すケースを追加し、非インスタンス化（`IsInstantiated == false`）と件数0を確認。入口ガードを完全カバー。

---

## T8. スタブのエラー注入パターンが不統一 ⬜ 未着手 【低】

### 問題
エラー注入が、静的フラグ方式（Exception/SystemError/OracleError）、接続文字列センチネル
`"ERROR_TRIGGER=THROW"` 方式（DisposableStub）、常時throw方式で混在。さらに `ExceptionStub` だけ
`TestServiceBase` を継承せず `IApiService`+`IAsyncDisposable` を直接実装。

### 対応案
- 一つの流儀（`TestServiceBase` 継承＋明示トリガ）に寄せると可読性が上がる。
  （ApiExecutor は `IApiService` 実装＋`(string,int)` コンストラクタを満たせばよいので統一は容易）

---

## T9. 静的可変状態の順序/並列依存 ⬜ 未着手 【低】

### 問題
`IsDisposed` / `YieldCount` / `IsInstantiated` の共有静的状態は、現状 xUnit がクラス内を直列実行する
ため安全だが、`[Collection]` 追加やスタブ再利用で壊れる脆さがある。

### 対応案
- インスタンス捕捉化（各テストでスタブの状態を個別保持）するか、制約をコメントで明記。

---

## T10. アサートが薄い（件数/NotEmpty のみ）⬜ 未着手 【低】

### 問題
`ServiceCountStub` は `Id = 0..n-1` を返すので、件数だけでなく**順序（ストリーミング整合）**まで
検証できるが、現状は件数のみ。実DBテストも `NotEmpty` のみ。

### 対応案
- `ServiceCountStub` 経由のテストで受信した `Id` 列の順序一致を assert すると、順序/取りこぼし回帰を拾える。

---

## T11. スタブ専用テストが実DB接続文字列(config)に依存 ⬜ 未着手 【低】

### 問題
`_connectionString` をフィールド初期化で config（`ServiceApi.Test.json`）から取得するため、
DBに触れないスタブテストも config 必須になり、結合設定に結合している。

### 対応案
- スタブ専用テストはダミー定数の接続文字列でよい（DisposableStub のセンチネル判定だけ留意）。

---

## T12. 例外伝播テストの重複 ⬜ 未着手 【スタイル】

### 問題
`RunAsync_異常系_リソース破棄の検証_01`（yield前にthrow）と `RunAsync_異常系_呼出し元に例外伝播_01`
（1件yield後にthrow）はほぼ同じ検証。

### 対応案
- 差分（throwタイミング＝0件目 vs ストリーム途中）は有意なので残してよいが、テスト名で区別を明確化する。

---

## 次回の進め方メモ

- 第1弾は ApiExecutor（本一覧 T1〜T12）。確認・採否に応じて状態列を更新する。
- 第2弾以降の候補：`ServiceBase`（コアループ/ReadRowsAsync/Dispose・DisposeAsync）、`TestServiceBase`、
  `B1Service`/`B1Service_Test`、Response 比較系（ResponseComparerBase ほか）。
  主要観点は **ApiExecutor と ServiceBase** に重点。
- T5 はログ抽象化（src review-notes §3）と連動するため、ログ方針確定後にまとめて再検討。

## 作業ログ
- 2026-05-31: ApiExecutor テストの初回レビュー（T1〜T12 を起票・提示のみ、状態は全件 未着手）。
- 2026-05-31: T1 を実装反映・テスト緑化（✅）。`Assert.ThrowsAsync<OracleException>` + `Number==12154` 検証へ強化。
  強化により `OracleErrorStub.CreateOracleException` のコンストラクタ選択バグ（`FirstOrDefault` で誤ったctorを掴み
  `TargetParameterCountException`／従来テストは誤った理由でパスしていた）が表面化し、5引数ctorの型指定取得に修正。
  `OracleException` が sealed である事実に基づきコメント訂正。修正前後は `#if true/#else/#endif` で一時残置。
- 2026-05-31: T2 を実装反映・テスト緑化（✅）。`ServiceCancelStub`/`ServiceTimeoutStub` に `IsDisposed` 追跡を追加し、
  キャンセル/タイムアウト時も `await using` でサービスが破棄されることを assert。変化点は `#if true/#else/#endif` で残置。
  コメントの「using var scope」表現の正確化は T6 へ送り。
- 2026-05-31: T6 を一部先行対応（🟡）。`RunAsync_異常系_ApiExecutor実行キャンセル確認_01` のコメント3件を訂正
  （①`using var scope`→`await using (service)`、②`B1Service_CancelStub`→`ServiceCancelStub`、③`B1Service 側のループ`→
  サービス側）。L41-43 ほか残りの旧DI/scope 表現は ApiExecutor 分を見終えた後にまとめて正確化予定。
- 2026-06-02: T3 を実装反映・テスト緑化（✅）。`FetchRowsCaptureStub` を追加し、(a)既定値100/(b)明示指定500/
  (c)ct位置指定で fetchRows 据え置き の3テストを追加。`using ServiceApi.Common;` 追加。引数順 src#7 の回帰ガードを確立。
- 2026-06-03: T4 を「現状維持＋軽微整理」で決着（✅）。DB起動前提のスモーク確認として4サービス分を残置（分離/分類・
  アサート強化・削除はいずれも見送り）。csproj の dangling 指定（`Services\B1\B1Service_Test.json`）を削除、スモーク群に
  前提コメントを追記。初回の「A1Service_Test.json 不在で壊れている」指摘は誤りと判明（A1_Test はコード生成方式で JSON 不要）と訂正。
