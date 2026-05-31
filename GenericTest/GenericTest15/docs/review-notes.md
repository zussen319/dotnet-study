# ServiceApi コードレビュー記録

- **対象**: `GenericTest15/src/ServiceApi` 配下（API側。Service / Request / Response 構成、Oracle 接続 SELECT）
- **呼び出し元（参考）**: `GenericTest15/main/B1Test/B1Test.cs` ほか A1/C1/C2 Test
- **環境**: .NET 10 / C# 最新, Oracle.ManagedDataAccess.Core
- **初回レビュー実施日**: 2026-05-30
- **最終更新日**: 2026-05-31

> このファイルは継続更新する「指摘事項の台帳」です。各項目の `状態` を更新しながら使ってください。

## 状態の凡例

- ✅ **実装反映済み** … コードに反映しビルド確認まで完了
- ✅ **対応方針確定** … 修正案コードまで合意済み（実装するだけ）
- 💬 **議論済み** … 方針は話したが実装コードは未提示／要判断
- ⬜ **未着手** … これから議論

---

## 総評（良い点）

- `ServiceBase` で「SQL文字列・パラメータバインド・行マッピング」を `Func`/`Action` で具象から注入する構造（テンプレートメソッド + ストラテジ）は拡張性が高い。
- `IAsyncEnumerable` ストリーミングで大量データでもメモリを抱え込まない。
- `OracleCommand` をループ外生成しカーソル/パース再利用、`FetchSize` 最適化、`reader` のループ内 `using`、`CancellationToken` 二重チェックなど Oracle/非同期の勘所を押さえている。
- リソース（SQL・メッセージ）の外部化、`record` による不変リクエスト/レスポンス。

---

## 指摘事項一覧

| # | 区分 | 概要 | 状態 |
|---|------|------|------|
| 1 | 中〜やや高 | C1/C2 が「コマンド再利用」設計を無効化 | ✅ 実装反映済み（2026-05-31） |
| 2 | 中 | `requests` が複数回列挙される | ✅ 対応（意図的に見送り・前提コメント追加 2026-05-31） |
| 3 | 中 | ライブラリ内の `Console.WriteLine` 直書き（ログ抽象化） | ✅ 対応（見送り・方針未確定のため 2026-05-31） |
| 4 | 中 | `Activator` リフレクション生成の型安全性／未使用 `MSG004` | ✅ 対応（意図的に完全見送り 2026-05-31） |
| 5 | 低〜中 | キャンセルが「異常終了」として記録される | ✅ 対応（見送り・プロトタイプのため 2026-05-31） |
| 6 | 低 | `Convert.ToDecimal` の桁あふれ・カルチャ（map系ローカル関数 mapEmp/mapMember/mapStaff） | ✅ 対応（見送り・プロトタイプのため 2026-05-31） |
| 7 | 低 | 引数順 `ct` が `fetchRows` より前 | ✅ 対応（現状維持・意図コメント追加 2026-05-31） |
| 8 | 低 | グルーピングが SQL の `ORDER BY` に暗黙依存 | ✅ 対応（見送り・認識済み 2026-05-31） |
| 9 | スタイル | B1 の `#if true / #else` 死コード | ✅ 対応（意図的に残置・後日削除予定 2026-05-31） |
| 10 | スタイル | その他の小さな点（後述） | 🟡 一部対応（MSG005・csproj整理済み／他は見送り・保留） |
| 補 | 仕様確認 | reader 同一インスタンス `yield` の注意 | ✅ 対応（コメント明記済み 2026-05-31） |

---

## 1. C1/C2 が「コマンド再利用」設計を無効化している ✅ 実装反映済み（2026-05-31）

### 問題
`C1Service` / `C2Service` は自前の `foreach (request in requests)` の中で
`ExecuteQueryAsync(sql, [request], ...)` と **1リクエストずつ単一要素配列で**基底を呼んでいた。
基底は呼ばれるたびに `using OracleCommand` を新規生成するため、C1/C2 では
リクエストごとにコマンドが作り直され、`ServiceBase` が狙う「同一SQLのカーソル/パース再利用」
（B1/A1 は全 `requests` を渡すので効く）が **効かなかった**。

### 採用した最終方針（議論の結論）
1. **コア検索を1本に統合**: 行を取得して集約する処理を、基底の単一コアループ
   `ExecuteQueryAsync(... groupFunc版)` に集約。`ExecuteReaderAsync` を呼ぶ場所は
   `ReadRowsAsync` の **ただ1か所** とする（重要な制約として維持する）。
2. **メソッド名を `ExecuteQueryAsync` に統一**: 1行→1レスポンス（map）も
   複数行→1レスポンス（group）も、すべて `ExecuteQueryAsync` のオーバーロード4本で表現。
   - (1) `mapFunc` / bindAction なし
   - (2) `mapFunc` / bindAction あり … 内部で map を group の特殊形に包んで (4) へ委譲
   - (3) `groupFunc` / bindAction なし
   - (4) `groupFunc` / bindAction あり … **唯一のコアループ**
3. **λの型はプログラマが明示**（方針1）: オーバーロード解決の曖昧化を運用で防ぐ。
   - A1/B1 は `Func<DbDataReader, TResponse>` 型の変数に代入してから渡す
   - C1/C2 の集約関数は async イテレータ＝ラムダで書けず必然的に型付きローカル関数になるため自動適合
   - `var` も原則使わない（例外は §10-静的化 の `enumerator` 1か所のみ。型名が長大なため `IDE0008` 抑制で現状維持）
4. **map / group の用語をコードに反映**:
   - 「map」= 1レコード→1オブジェクト変換、「group」= 複数レコードを条件で1オブジェクトに集約
   - 各サービスでは目的に即した具体名にする（汎用名は基底のみ）
     - B1: `mapFunc` → `mapEmp`
     - C1: `groupFunc` → `groupDept` / 集約変数 `response` → `dept` / map は `mapEmp`
     - C2: `groupFunc` → `groupDept` / 集約変数は `dept`・`member`（既存維持）/ map は `mapMember`・`mapStaff`
     - `groupByDept` も候補だったが、プロトタイプにつき簡潔な `groupDept` を採用
   - map系ローカル関数は `ExecuteAsync` 内に置き「ここでしか使わない」をスコープで表現。
     そのため `groupDept` は `static` を外して（外側の map ローカル関数を参照するため）非static とする。

### コメント記述規約（このプロトタイプ以降の指針）
処理コメントは原則 **「1行目=何を / 2行目=ポイント(how) / 3行目=ねらい(why)」** の3層で記述し、
詳細な背景は直後の `/* */` 本文に集約する（簡潔な見出し＝行コメント、詳細＝ブロックの役割分担）。
- ルールは厳密に強制せず、**伝達すべき内容**を優先する。
- 重視するのは「**コメントと実装の不一致がないこと**（修正漏れ防止）」と
  「**記載内容に誤り・誤解を招く説明がないこと**（ベストプラクティス等）」。
- 必須は1行目のみ。ポイント・ねらいが無い単純処理に無理に3行付けない。

### 実装ファイル（実際の最終コードはリポジトリを参照）
反映済みファイル：
- `src/ServiceApi/Services/ServiceBase.cs`（コア統合・`ReadRowsAsync` 新設）
- `src/ServiceApi/Services/C1/C1Service.cs`（`groupDept` + `mapEmp`）
- `src/ServiceApi/Services/C2/C2Service.cs`（`groupDept` + `mapMember`/`mapStaff`）
- `src/ServiceApi/Services/B1/B1Service.cs`（`mapFunc`→`mapEmp` のみ。ロジック変更なし）

> ※ A1Service.cs は検討用バージョンのため本対応の対象外（担当者が別途整理予定）。
> ※ C1/C2 は旧コードを暫定的に `#if true / #else / #endif` で残している（指摘9 で旧コード削除予定）。
> ※ ビルド確認済み（2026-05-31 時点で 0 Warning / 0 Error）。

### 基底コアの要点（参考スニペット）
```csharp
// (4) 唯一のコアループ：コマンドはループ外で1回だけ生成し、リクエスト間はパラメータのみ入替
protected virtual async IAsyncEnumerable<TResponse> ExecuteQueryAsync(
    string sql,
    IEnumerable<TRequest> requests,
    Action<OracleParameterCollection, TRequest> bindAction,
    Func<IAsyncEnumerable<DbDataReader>, IAsyncEnumerable<TResponse>> groupFunc,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    if (this.Connection is { State: ConnectionState.Closed }) { await this.Connection.OpenAsync(ct); }
    using OracleCommand command = new(sql, this.Connection) { BindByName = true };

    foreach (TRequest request in requests)
    {
        ct.ThrowIfCancellationRequested();
        command.Parameters.Clear();
        bindAction(command.Parameters, request);

        await foreach (TResponse response in groupFunc(ReadRowsAsync(command, ct)))
        {
            yield return response;
        }
    }
}

// map（1行→1レスポンス）は group の特殊形として表現し、(4) へ委譲（overload(2)）
//   async IAsyncEnumerable<TResponse> groupFunc(IAsyncEnumerable<DbDataReader> rows)
//   { await foreach (var reader in rows) { yield return mapFunc(reader); } }

// ★ExecuteReaderAsync を実行する唯一の場所
private async IAsyncEnumerable<DbDataReader> ReadRowsAsync(
    OracleCommand command,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    using OracleDataReader reader = await command.ExecuteReaderAsync(ct);
    reader.FetchSize = reader.RowSize * this.FetchRows;       // FetchSize最適化
    while (await reader.ReadAsync(ct)) { yield return reader; } // 同一readerを即時消費（保持禁止）
}
```

---

## 2. `requests` が複数回列挙される ✅ 対応（意図的に見送り・前提コメント追加 2026-05-31）

### 問題
`ApiExecutor` が `requests.Any()`（先頭1件を列挙）した後、サービス側で `foreach` が再度列挙する。
遅延評価の `IEnumerable`（LINQ・`yield`・`IQueryable` 等）を渡されると二重列挙＝二重副作用の温床。

### 検討した対応案
**ApiExecutor 側だけで対処可能**（サービス側の変更不要）。入口で1回だけ実体化する案：
```csharp
IReadOnlyList<TRequest> materializedRequests =
    requests as IReadOnlyList<TRequest> ?? requests?.ToList() ?? [];
if (materializedRequests.Count == 0) { yield break; }
// 以降は materializedRequests を service.ExecuteAsync(...) に渡す
```

### 最終判断：意図的に見送り（コード変更なし＋前提コメント追加）
費用対効果を検討した結果、**実体化（ToList）は行わず、前提をコメントで明示する**方針を採用。

理由：
- 二重列挙が実害になるのは `requests` が**遅延シーケンス**のときだけ。本 API の `requests` は
  **検索条件（数件〜数百件規模）**で、呼び出し元は `config.Get<IEnumerable<TRequest>>()`＝実体 `List`。
  遅延 `IEnumerable` を渡す使い方は現実的に想定しにくく、発生確率が低い。
- 仮に発生しても再評価されるのは「検索条件の数件」のみ。数万〜数百万件の**リターンデータ（Response）側とは無関係**。
- `as IReadOnlyList<T> ?? ToList() ?? []` の1行は、防ぐリスクの小ささに対し
  恒久的に全員が読む基盤コードの**解読コスト（可読性低下）**が見合わない。
- レビュー指摘は「便益 > コスト」のときに直すもの。本件は逆のため**見送りも正しい設計判断**。

採用したコメント（`ApiExecutor.RunAsync` 冒頭）：
```csharp
// ※requestsは「複数回列挙しても安全なコレクション（List/配列等）」を前提とする
//   検索条件（数件～数百件規模）であり、遅延IEnumerableは想定しないため
//   入口での実体化（ToList）は行わない
if (requests is null || !requests.Any()) { yield break; }
```

### 補足（用語・コスト）
- **遅延シーケンス**：列挙のたびに毎回ゼロから再計算される `IEnumerable`（LINQの `Select`/`Where`、
  `yield` メソッド、`IQueryable`）。複数回列挙すると処理・副作用・DBアクセスが二重化する。
  `List`/配列は即時評価（確定済み）なので複数回列挙しても安全。
- **`ToList()` のコスト**：要素の参照を新配列にコピーするだけ（要素の複製はしない）。検索条件規模では
  ナノ〜マイクロ秒で体感差・リソース影響はゼロ。DBアクセス1回（ミリ秒〜）より桁違いに軽い。
  → コスト面では実体化しても問題なかったが、本件は「可読性」を優先して見送り。

> 関連：§10-静的化 のサンプルコードには当初案の `materializedRequests` 実体化が残っている。
> 指摘10（static化）を実装する際は、この見送り判断に合わせて実体化行を外す／注記すること。

---

## 3. ライブラリ内の `Console.WriteLine` 直書き ✅ 対応（見送り・方針未確定のため 2026-05-31）

`ApiExecutor` が開始/終了/エラーを `Console` に直接出力している。呼び出し元（メイン）は
`Microsoft.Extensions.Logging` を参照しているのに、API側はログ先・レベルを制御できない。

**検討した対応案**: `ILogger` を注入する、または進捗/エラーを構造化イベント（コールバック）で
呼び出し元に返し、出力方法は呼び出し側に委ねる。

### 最終判断：見送り（現時点）
- プロジェクト全体の**ロギング方針が未確定**で、メッセージ出力自体が暫定対応の段階。
  方針未確定のまま `ILogger` 等を入れると、方針確定後に手戻りになる。
- 技術的難度は低く、**方針が決まってからでも低コストで対応可能**。
- **指摘10（static化）と連動**：DI するならインスタンス、しないなら static。
  ログ方針を先に決めてから static 化を判断するのが望ましい（→ §10-静的化 も保留中）。
- ロギング方針が固まった段階で再着手する。

---

## 4. `Activator` リフレクション生成の型安全性／未使用 `MSG004` ✅ 対応（意図的に完全見送り 2026-05-31）

`(TService)Activator.CreateInstance(typeof(TService), connectionString, fetchRows)` は
型制約ではコンストラクタ `(string, int)` の存在を保証できず、署名違いの型で実行時 `MissingMethodException`。
一方、まさにこの用途と思われる `MSG004`（"Type {0} does not properly implement IApiService."）が
resx に定義済みだが、`MessageId` 定数が無く未使用。

**検討した対応案**: `CreateInstance` を try/catch して `MSG004` でラップする／
または型安全な `Func<string,int,TService>` ファクトリ引数に変更してリフレクション排除。

### 最終判断：完全見送り（コード変更なし）
2要素に分けて検討し、いずれも見送りと決定。

- **要素A（Activator の型安全性）**: 実行時例外になるのは「`(string,int)` コンストラクタを持たない
  `TService` を渡したとき」だけ。`TService` を渡すのは開発者自身（外部入力ではない）で、新サービス追加時に
  コンストラクタ規約を守れば発生せず、開発時に必ず気づくレベル。発生確率が低く、対策（ファクトリ引数化等）の
  複雑さ・可読性コストが見合わないため見送り（指摘2と同論理）。
- **要素B（未使用 MSG004）**: デッドリソースだが、**担当者が未使用であることを把握済み**のため
  意図的に残置（B-1/B-2 の整理も行わない）。将来 要素A に対応する場合に MSG004 を活用できる。

> 記録：MSG004 は「定義済みだが未使用」の状態であることを認識のうえ、意図的に残している。
> 将来この欠番に気づいても、本判断（意図的残置）を踏まえること。
> 関連：`MessageId.cs` の MSG005 コメント崩れ（`detected./// </summary>`）は指摘10の別項目で扱う。

---

## 5. キャンセルが「異常終了」として記録される ✅ 対応（見送り・プロトタイプのため 2026-05-31）

キャンセル時、catch で `MSG005` をログ→`throw` し、`finally` でも `isCompleted==false` のため
`MSG003`（異常終了）が出る。正常なキャンセルが「例外で異常終了」と二重に記録され紛らわしい。

**検討した対応案**: キャンセル専用フラグで終了ログを分岐する。

### 最終判断：見送り（現時点）
- 問題の本質は「**ログの見え方が紛らわしい**」という表示の話で、**処理自体は正しく動く**
  （キャンセルは正しく伝播する）。プロトタイプ段階で本番想定の厳密対応は不要。
- **指摘3（ロギング方針）を見送った**ため、ログまわりの整形は方針確定後にまとめて扱うのが自然。
  → **指摘3 と一緒に再検討する**のが筋。

---

## 6. `Convert.ToDecimal` の桁あふれ・カルチャ ✅ 対応（見送り・プロトタイプのため 2026-05-31）

各 map 系（`mapEmp`/`mapMember`/`mapStaff` 等）の `Convert.ToDecimal(r["…"])` は、Oracle `NUMBER(38)` の
ように `System.Decimal`（約28〜29桁）を超える値で `OverflowException` の可能性。EMP スキーマでは実害なし。

**検討した対応案**: `OracleDataReader.GetOracleDecimal` 等の利用。あわせて拡張メソッド
（`GetStringOrEmpty`/`GetDecimalOrNull`）の採用で重複削減・可読性向上。

### 最終判断：見送り（現時点）
- `OverflowException` は `System.Decimal` 超の巨大数値でのみ発生し、**EMP スキーマでは起こらない**。
  汎用基盤として将来の大きな数値列に備える話で、プロトタイプ段階では時期尚早。
- 将来「本番想定」フェーズに入ったら、**拡張メソッド導入（map系の重複削減）と桁あふれ対策をセットで**
  再検討するとよい。

---

## 7. 引数順 `ct` が `fetchRows` より前 ✅ 対応（現状維持・意図コメント追加 2026-05-31）

`RunAsync(..., CancellationToken ct = default, int fetchRows = ...)`。慣例上 `CancellationToken` は
最後に置くのが一般的。

### 最終判断：現状維持（ロジック変更なし）＋ 意図コメント追加
慣習より「呼び出し側の指定頻度で並べる」設計意図を優先。明確な根拠があり妥当と判断。

- **設計意図**：
  - `fetchRows` は API 内部のチューニング用で、メイン側（別担当）は原則デフォルト依存（指定は最終手段）＝頻度低。
  - `ct` はメイン側で指定する頻度が相対的に高い。
  - 「**指定頻度が高い引数を前**」に置くと、`ct` だけ渡すケースで `fetchRows` を飛び越える
    名前付き引数が不要になり、頻度の高い操作が簡潔になる（オプション引数設計の定石）。
- 慣習を外す以上、**意図を `ApiExecutor.RunAsync` のコメントに明記**して後任の誤った「修正」を防止（反映済み）。
- 補足（任意の推奨）：呼び出し側は `ct:` / `fetchRows:` と**名前付き**で渡すと意図が伝わりやすい。
- 大がかりな再設計（fetchRows を引数から外しオプション化する案）はプロトタイプ段階では過剰のため見送り。

---

## 8. グルーピングが SQL の `ORDER BY` に暗黙依存 ✅ 対応（見送り・認識済み 2026-05-31）

C1/C2 の「ブレイク判定」グルーピングは SQL の `ORDER BY` 前提。並び順が変わると静かに重複グループが発生。

### 最終判断：見送り（担当者が認識済み）
- 指摘1の修正後コードで SQL コメントに「この並び順が前提」を明記済み。
- ORDER BY 依存は担当者が把握しており、プロトタイプ段階では簡易ガード等は不要と判断。

---

## 9. B1 の `#if true / #else` 死コード ✅ 対応（意図的に残置・後日削除予定 2026-05-31）

`B1Service.cs`（および指摘1で C1/C2 にも）残る `#if true / #else` は、本番コードとしては死コードだが、
**プロトタイプ段階では意図的に残している**。

### 最終判断：意図的に残置（後日削除）
- 数日後に今回の修正を振り返れるよう、旧コードを `#if true/#else/#endif` で一時保持する運用。
- 「完全に不要」と判断できた段階で担当者が削除する。
- 対象：`B1Service.cs` / `C1Service.cs` / `C2Service.cs`（C1/C2 は指摘1の旧コード）。

---

## 10. その他の小さな点

- **MSG004 未使用 + `MessageId` に MSG004 定数なし**: → §4 で「意図的に完全見送り（把握済みで残置）」に決着。✅
- **`MessageId.cs` の MSG005 XMLコメント誤記** (`detected./// </summary>`): **修正済み（2026-05-31）**。
  → `/// <summary>Cancellation request detected.</summary>` に修正。✅
- **`B1Response.DEPTNO` が `decimal?`**: 検索キーで必ず値があり `B1Request.DEPTNO`（非null）と非対称。
  → **見送り**（プロトタイプにつき重視せず。本番開発時に考慮）。✅
- **空リクエスト時のログ未実装**: `yield break` 前に「メッセージ出力すべき」とコメントのみ。開始/終了ログも出ない。
  → ログ方針（指摘3）と連動するため保留。⬜
- **`ServiceApi.csproj` の `<Folder Include>`**: VS が残した空フォルダ用エントリで実在と不整合（A1 欠落・Responses 歯抜け等）。
  → **削除済み（2026-05-31）**。各フォルダに実ファイルがあるためフォルダは消えず、ビルドも問題なし。
  リソース用 `<ItemGroup>`（Compile/EmbeddedResource）は残置。✅
- **`ApiExecutor` の static 化**: 状態を持たないため可能。💬 **保留（2026-05-31）**。
  - 単に `static` を付けるだけでは不十分：①クラス＋メソッド両方 static 化、②呼び出し側（メイン側 B1/A1/C1/C2 Test）の破壊的変更（`new ApiExecutor()` 廃止→`ApiExecutor.RunAsync(...)`）が必要、③指摘3（ログDI）と両立しない。
  - メイン側は別担当のため一人で完結できず、かつログ方針確定で覆る可能性があるため、**指摘3とセットで後日判断**。完成版コードは §10-静的化 を参照。
- **`TestServiceBase` の JSON ファイル名 `GetType().Name` 依存**: クラス名＝JSONファイル名という、コンパイラ検査の効かない暗黙結合。リネーム時にビルドは通るが実行時 `FileNotFound`。
  → **見送り（担当者が認識済み 2026-05-31）**。プロトタイプ段階では対応不要と判断。
  （将来対応する場合の選択肢：コメントで規約明示／ファイル名を派生側 override プロパティ化。
  後者はコンストラクタ引数を増やすと `Activator` 生成シグネチャ `(string,int)` と衝突する点に注意）

### §10-静的化: ApiExecutor.cs 完成版（指摘2 の実体化 + static 化を統合）💬

> `static class` にするとメンバーは全て static 必須のため、`RunAsync` も `static` になる。
> **呼び出し側は `new ApiExecutor()` をやめ `ApiExecutor.RunAsync(...)` に変更が必要（破壊的変更）。**
> メイン側は別担当のため、static 化採用時は全 Test プロジェクトの呼び出し修正を事前共有すること。

```csharp
using Oracle.ManagedDataAccess.Client;
using ServiceApi.Common;
using ServiceApi.Requests;
using ServiceApi.Resources.Messages;
using ServiceApi.Responses;
using ServiceApi.Services;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ServiceApi;

// 状態を持たないため static クラスとする
public static class ApiExecutor
{
    [SuppressMessage("Style", "IDE0008")]
    public static async IAsyncEnumerable<TResponse> RunAsync<TService, TRequest, TResponse>(
            string connectionString,
            IEnumerable<TRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default,
            int fetchRows = ApiConstants.DefaultFetchRows)
            where TService : class, IApiService<TRequest, TResponse>
            where TRequest : RequestBase
            where TResponse : ResponseBase
    {
        // リクエストを入口で1回だけ実体化（指摘2: 二重列挙の防止）
        IReadOnlyList<TRequest> materializedRequests =
            requests as IReadOnlyList<TRequest> ?? requests?.ToList() ?? [];

        if (materializedRequests.Count == 0)
        {
            // TODO(指摘10): 0件時にも何らかのログを出すべき
            yield break;
        }

        TService service =
            (TService)Activator.CreateInstance(typeof(TService), connectionString, fetchRows)!;

        bool isCompleted = false;

        await using (service)
        {
            Console.WriteLine(
                MessageResourceProvider.GetMessage(MessageId.MSG001, typeof(TService).Name));

            var enumerator =
                service.ExecuteAsync(materializedRequests, ct).WithCancellation(ct).GetAsyncEnumerator();

            try
            {
                while (true)
                {
                    TResponse response;
                    try
                    {
                        if (!await enumerator.MoveNextAsync()) { break; }
                        response = enumerator.Current;
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine(MessageResourceProvider.GetMessage(MessageId.MSG005));
                        throw;
                    }
                    catch (Exception ex)
                    {
                        string msg = ex switch
                        {
                            OracleException ox => $"[Database Error] Code: {ox.Number}, Message: {ox.Message}",
                            _ => $"[System Error] {ex.Message}"
                        };
                        Console.WriteLine(msg);
                        throw;
                    }

                    yield return response;
                }
                isCompleted = true;
            }
            finally
            {
                await enumerator.DisposeAsync();

                string message = (isCompleted
                    ? MessageResourceProvider.GetMessage(MessageId.MSG002)
                    : MessageResourceProvider.GetMessage(MessageId.MSG003));
                Console.WriteLine(message);
            }
        }
    }
}
```

呼び出し側（メイン）の変更イメージ:

```csharp
// 変更前
ApiExecutor executor = new();
responseStream = executor.RunAsync<B1Service_Test, B1Request, B1Response>(connStr, requests, ct);

// 変更後
responseStream = ApiExecutor.RunAsync<B1Service_Test, B1Request, B1Response>(connStr, requests, ct);
```

---

## 補. 仕様確認したい点（バグではない可能性）✅ 対応（コメント明記済み 2026-05-31）

`ServiceBase.ReadRowsAsync` は毎行 **同一の `DbDataReader` インスタンス**を `yield return` する
（`Read()` でカーソルが進むだけ）。即時消費（mapFunc / groupFunc）には問題ないが、将来 `ToListAsync()` 等で
蓄積すると全要素が同じ（最終/クローズ済み）reader を指す罠になる。

### 最終判断：案A（コメントで注意喚起）を採用・対応済み
- `ReadRowsAsync` のコメントに「**yield return する reader は行ごとに同一インスタンス。即時消費専用
  （保持・蓄積は禁止）**」を明記済み。
- `ReadRowsAsync` は `private` で影響範囲が限定的なため、仕組みでの防止（案C: reader をコピーして返す）は
  ストリーミングの利点を損なうため不採用。プロトタイプ段階ではコメント注意喚起で十分。

---

## 次回の進め方メモ

- **指摘1 実装反映済み／指摘2〜9 決着済み・指摘10 一部対応（2026-05-31）**。残る未決は **補** と、指摘10の保留分（static化・空リクエスト時ログ・TestServiceBase JSON名依存）。
- ロギング方針が決まり次第、**指摘3（ログ抽象化）＋指摘5（キャンセルログ）＋指摘10の空リクエスト時ログ＋§10-静的化 をセットで再検討**する。
- 「本番想定」フェーズに入ったら、**指摘6（桁あふれ）＋拡張メソッド導入（map系の重複削減）**をセットで再検討する。
- 後日対応予定：**指摘9（`#if true/#else` 削除）** … B1/C1/C2 の旧コードを「完全に不要」と判断した段階で削除。
- 残る議論候補: **補（reader 同一インスタンス `yield` の注意）** … 唯一の純粋な未着手項目。

## 作業ログ
- 2026-05-30: 初回レビュー（指摘1〜10・補を起票）。指摘1・2・10静的化の対応方針を確定。
- 2026-05-31: 指摘1を実装反映・ビルド確認・本ノート更新。`ExecuteQueryAsync` 名称統一／`ExecuteReaderAsync` 単一箇所化／map・group 用語と命名規約／コメント3層規約を確定。
- 2026-05-31: 指摘2を「意図的に見送り（前提コメント追加のみ）」で決着。requestsは検索条件でList前提・遅延非想定のため実体化せず。
- 2026-05-31: 指摘3を見送り（ロギング方針未確定・暫定対応・低難度のため、方針確定後に再着手）。
- 2026-05-31: 指摘4を完全見送り（要素A=Activator型安全性は発生確率低・対策コスト過大、要素B=未使用MSG004は把握済みで意図的残置）。
- 2026-05-31: 指摘5・6を見送り（プロトタイプにつき本番想定の厳密対応は不要。5はログ表示の話で指摘3と連動、6はEMPスキーマでは桁あふれ非発生）。
- 2026-05-31: 指摘7を現状維持＋意図コメント追加で決着（ct/fetchRowsの引数順は「指定頻度が高い引数を前」の設計意図を優先）。
- 2026-05-31: 指摘8を見送り（ORDER BY依存は認識済み・SQLコメントで明記済み）。指摘9を意図的残置（#if で旧コード一時保持・後日削除）。
- 2026-05-31: 指摘10のMSG005コメント崩れを修正。B1Response.DEPTNOは見送り（本番時に考慮）。static化は指摘3とセットで保留。
- 2026-05-31: 補（reader同一インスタンス）を案A（コメント注意喚起）で対応完了。指摘10のcsproj `<Folder Include>` を削除（ビルド確認済み）。
- 2026-05-31: 指摘10の `TestServiceBase` JSON名依存を見送り（担当者が認識済み・プロトタイプ段階では対応不要）。
