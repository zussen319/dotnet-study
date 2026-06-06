# C# モダン構文 推奨メモ

- **目的**: .NET Framework 時代より後に追加された「新しい記述スタイル」を、可読性・安全性を損なわない範囲で積極採用するための一般的な推奨メモ。
- **位置づけ**: コーディング時の参考メモ（強制ルールではない）。判断軸は「新しいから使う」ではなく「**読みやすく・安全になるから使う**」。

> ⚠ バージョン表記の注意：`??` や `?.`、オブジェクト初期化子などは C# 3〜8（.NET Framework 時代にも一部利用可）だが、
> 本メモでは「モダンC#で常用される記述スタイル」をまとめて扱う。

---

## 最優先：ぜひ常用したいもの

### 1. null 関連演算子（C# 6〜8）
```csharp
obj?.Member            // null条件 (C#6)：objがnullならnullを返す（例外回避）
a ?? b                 // null合体 (C#6)：aがnullならb
x ??= ComputeDefault() // null合体代入 (C#8)：xがnullの時だけ代入
list?.Count ?? 0       // 組み合わせ：nullなら0
```

### 2. 文字列補間（C# 6）／生文字列リテラル（C# 11）
```csharp
$"[{messageId}] {value}"            // 文字列補間
$"Name: {name,-10} Age: {age:D3}"   // 整形指定も可

string sql = """
    SELECT EMPNO, ENAME
    FROM EMP
    WHERE DEPTNO = :DEPTNO
    """;                            // 生文字列リテラル：エスケープ不要（SQL/JSON/ログ整形に有用）
```

### 3. switch 式（C# 8）
`switch` 文より簡潔。
```csharp
string msg = ex switch
{
    OracleException ox => $"DB Error: {ox.Number}",
    _ => $"System Error: {ex.Message}"
};
```

### 4. パターンマッチング（C# 7〜11）
近年で最も強化された分野。
```csharp
if (obj is Customer c) { use(c); }            // 型パターン (C#7)
if (conn is { State: ConnectionState.Open })  // プロパティパターン (C#8)
if (x is null) / if (x is not null)           // null / 否定パターン (C#9)
if (r[col] is DBNull or null)                 // or パターン (C#9)
if (n is > 0 and < 100)                       // 関係 + 論理パターン (C#9)
if (list is [])  / is [var first, ..]         // リストパターン (C#11)
```

### 5. ターゲット型 new（C# 9）
型名を右辺で繰り返さない。
```csharp
OracleCommand command = new(sql, conn);
List<int> list = new();
```

---

## 強く推奨：データ・型まわり

### 6. オブジェクト初期化子（C# 3）
コンストラクタ呼び出しと同時に、プロパティ／フィールドを `{ }` 内で設定できる。
要素の区切りは `,`（末尾にセミコロンは付けない）。
```csharp
var obj = new MyObject { Member1 = "A", Member2 = "B" };

// コレクション初期化子（要素をまとめて設定）
var list = new List<int> { 1, 2, 3 };

// ネストした初期化も可
var dept = new Dept
{
    DeptNo = 10,
    Members = { member1, member2 }   // 既存コレクションへの追加
};
```
- ターゲット型 new（#5）、`init`/`required`（#7）、コレクション式（#9）と組み合わせると真価を発揮する。
```csharp
// ターゲット型 new + オブジェクト初期化子
MyObject obj = new() { Member1 = "A", Member2 = "B" };
```

### 7. record / record struct（C# 9 / 10）
不変データに最適。値ベースの等価比較・`with` 式・`ToString()` が自動生成。
```csharp
public record Request { public required decimal DeptNo { get; init; } }
var copy = req with { DeptNo = 20 };   // 一部だけ変えた複製
```

### 8. init アクセサ / required（C# 9 / 11）
生成後は不変、かつ必須プロパティを強制。
```csharp
public required decimal Empno { get; init; }
```

### 9. ファイルスコープ namespace（C# 10）
ネストを1段減らせる。
```csharp
namespace MyApp.Services;   // 末尾セミコロン、波括弧不要
```

### 10. コレクション式 `[]`（C# 12）
配列・List・Span などを `[]` で統一的に初期化。
```csharp
int[] a = [1, 2, 3];
List<int> b = [];
int[] c = [..a, 4, 5];   // スプレッド
```

### 11. プライマリコンストラクタ（C# 12）
クラスにも対応（以前は record のみ）。
```csharp
public class MyService(string connectionString, int fetchRows = 100)
    : ServiceBase(connectionString, fetchRows) { }
```

---

## 余裕があれば取り入れたいもの

### 12. 式形式メンバー（expression-bodied member, C# 6〜7）
```csharp
public static string GetSql(string id) => _instance.GetString(id);
```

### 13. using 宣言（C# 8）
波括弧なしの using。スコープ末尾で自動 Dispose。
```csharp
using var reader = await cmd.ExecuteReaderAsync(ct);
// （ブロックの } まで生存）
```

### 14. ローカル関数（C# 7）
ラムダより自己文書的で再帰も可。

### 15. タプルと分解（C# 7）
軽量な複数戻り値。
```csharp
(string name, int age) GetUser() => ("Tanaka", 30);
var (name, age) = GetUser();
```

### 16. nameof（C# 6）
文字列リテラルを避けてリファクタ耐性 UP。
```csharp
throw new ArgumentOutOfRangeException(nameof(fetchRows), "...");
```

### 17. グローバル using / 暗黙的 using（C# 10）
`<ImplicitUsings>enable</ImplicitUsings>` で `System` 等が自動 import。
共通 using は `GlobalUsings.cs` に集約可能。

---

## 補足：ガード用の新 API（.NET 7 / 8）

例外スローのボイラープレートを置き換える静的ヘルパー。意図がメソッド名で自己説明的になる。
```csharp
ArgumentNullException.ThrowIfNull(obj);                       // .NET 6
ArgumentException.ThrowIfNullOrEmpty(connectionString);       // .NET 7
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fetchRows); // .NET 8
```
- 注意：標準の例外メッセージは英語。**日本語メッセージを保持したい場合は従来の明示 throw を使う**。

---

## 採用判断の指針（重要）

- **判断軸**：「新しいから使う」ではなく「**読みやすく・安全になるから使う**」。
- パターンマッチや switch 式を**入れ子で凝りすぎる**と、かえって読みにくくなる。複雑なら通常の `if`/`switch` 文の方が明快。
- チームで「演算子・`=>` の改行位置」などスタイルを固定したい場合、C# 標準（**演算子・`=>` は継続行の行頭側**）に合わせると IDE の自動整形（VS / `dotnet format`）と衝突しない。
  - ラムダ式＋ブロック本体（`(p, req) => { ... }`）で `=>` が行末に来るのは標準的な書き方であり、上記の対象外。
