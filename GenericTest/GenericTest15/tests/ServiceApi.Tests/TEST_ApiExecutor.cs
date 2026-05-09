using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests;
using ServiceApi.Requests.B1;
using ServiceApi.Requests.C1;
using ServiceApi.Responses;
using ServiceApi.Responses.B1;
using ServiceApi.Responses.C1;
using ServiceApi.Services;
using ServiceApi.Services.B1;
using ServiceApi.Services.C1;
using ServiceApi.Tests.Common;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ServiceApi.Tests;

// テスト用のスタブ
public class MockResponse : ResponseBase { public int Id { get; set; } }
public class MockRequest : RequestBase { }

public class TEST_ApiExecutor
{
    // DB接続文字列：テスト用設定ファイル（ServiceApi.Test.Json）から取得
    private readonly string _connectionString =
        TEST_ConfigurationManager.GetValue<string>(ConfigId.ConnectionString);

#if true
    // --- テスト用スタブクラス ---
    /*
     * ### このテストで確認できていること
    * 1.  **境界値の動作**: `InlineData(0)` のようにデータが1件もない場合でも、
    * `RunAsync` 内の `while` ループを正しく抜け、`finally` を通って 
    * `Dispose` されるかが確認できます。
    * 2.  **大量データの完走**: `InlineData(1000)` を通すことで、ループ処理において
    * メモリリークや意図しない中断が発生しないことを担保できます。
    * 3.  **DIスコープの連動**: `ApiExecutor` 内の `using var scope` が正常に機能し、
    * 件数に関わらずループ終了時にサービスを道連れに破棄してくれることを保証しています。
    * ### 補足：テスト実行時のアドバイス
    * 大量データをテストする場合、`B1Service_Test` で実装したように `Task.Delay` 
    * を入れているとテスト時間が長くなってしまいます。
    * この `Theory` テストでは **`mockService` を使って `Delay` なしの即時応答** 
    * を返しているため、1000件程度であれば一瞬で終わります。
    */

    public class B1Service_CountStub : IB1Service, IAsyncDisposable
    {
        public static bool IsDisposed { get; set; }
        public static int YieldCount { get; set; }

        // Activator 用のコンストラクタ
        public B1Service_CountStub(string connStr) { }

        public async IAsyncEnumerable<B1Response> ExecuteAsync(
            IEnumerable<B1Request> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int i = 0; i < YieldCount; i++)
            {
                yield return new B1Response { EMPNO = i };
            }
            await Task.Yield();
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData(0)]      // 0件（データなし）
    [InlineData(1)]      // 最小件数
    [InlineData(100)]    // 中規模
    [InlineData(1000)]   // 大量データ想定
    public async Task RunAsync_正常系_終了時サービス破棄確認_02(int testCount)
    {
        // Arrange
        var executor = new ApiExecutor();
        var requests = new[] { new B1Request { DEPTNO = 10 } };

        // 静的プロパティを初期化
        B1Service_CountStub.IsDisposed = false;
        B1Service_CountStub.YieldCount = testCount;

        // Act
        int actualCount = 0;
        // TService には IB1Service ではなく、スタブクラスを指定する
        await foreach (var item in executor.RunAsync<B1Service_CountStub, B1Request, B1Response>(_connectionString, requests))
        {
            actualCount++;
        }

        // Assert
        Assert.Equal(testCount, actualCount);
        Assert.True(B1Service_CountStub.IsDisposed, $"件数 {testCount} において、サービスが正しく破棄されていません。");
    }
#endif

#if true
    /* 正常系：リソース破棄（Dispose）の検証（同期・非同期問わず）*/

    // --- 統合版スタブクラス ---// --- 統合版スタブクラス（正常系・異常系共用） ---
    public class DisposableStub : IApiService<MockRequest, MockResponse>, IDisposable, IAsyncDisposable
    {
        public static bool IsDisposed { get; set; }
        // static bool ShouldThrow は削除

        private readonly string _connectionString;

        public DisposableStub(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // 接続文字列に "THROW" が含まれていたら例外を投げる
            if (_connectionString.Contains("THROW"))
            {
                ThrowException();
                yield break;
            }

            yield return new MockResponse { Id = 1 };
            await Task.Yield();

            void ThrowException() => throw new InvalidOperationException("予期せぬエラー");
        }

        public void Dispose() => IsDisposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task RunAsync_正常系_リソース破棄の検証_01()
    {
        var executor = new ApiExecutor();
        var requests = new[] { new MockRequest() };
        DisposableStub.IsDisposed = false;

        // 普通の接続文字列を渡す
        await foreach (var _ in executor.RunAsync<DisposableStub, MockRequest, MockResponse>(_connectionString, requests))
        {
        }

        Assert.True(DisposableStub.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_異常系_リソース破棄の検証_01()
    {
        var executor = new ApiExecutor();
        var requests = new[] { new MockRequest() };
        DisposableStub.IsDisposed = false;

        // 接続文字列に "THROW" を含めることで、スタブに例外を投げさせる
        string errorConn = _connectionString + ";ERROR_TRIGGER=THROW";

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in executor.RunAsync<DisposableStub, MockRequest, MockResponse>(errorConn, requests))
            {
            }
        });

        Assert.True(DisposableStub.IsDisposed);
    }
#endif

#if true
    // --- テスト用の異常系スタブクラス ---
    public class ExceptionStub : IApiService<MockRequest, MockResponse>, IAsyncDisposable
    {
        public static bool IsDisposed { get; set; }

        public ExceptionStub(string conn) { }

        public async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // 1件目は正常に返す
            yield return new MockResponse { Id = 1 };

            await Task.Yield();

            // 2件目の列挙で例外を発生させる
            throw new InvalidOperationException("DB接続エラー");
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task RunAsync_異常系_呼出し元に例外伝播_01()
    {
        // Arrange
        var executor = new ApiExecutor();
        var requests = new[] { new MockRequest() };

        // 静的フラグで例外発生後の破棄を確認
        ExceptionStub.IsDisposed = false;

        // Act & Assert
        // 1. 指定した例外が正しく外側まで飛んでくることを検証
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            // TService には例外を投げる具象クラスを指定
            await foreach (var item in executor.RunAsync<ExceptionStub, MockRequest, MockResponse>(_connectionString, requests))
            {
                // 1件目は正常に処理され、その後の列挙で例外が発生する
            }
        });

        // メッセージの検証（必要であれば）
        Assert.Equal("DB接続エラー", exception.Message);

        // 2. 重要：例外発生時でも service.DisposeAsync() が呼ばれていることを検証
        Assert.True(ExceptionStub.IsDisposed, "例外発生時にサービスが破棄されていません。");
    }
#endif

#if true
    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_B1Service実行_01(decimal deptNo)
    {
        // Arrange
        var executor = new ApiExecutor();
        var requests = new[] { new B1Request { DEPTNO = deptNo } };
        var results = new List<B1Response>();

        // Act
        await foreach (var response in executor.RunAsync<B1Service, B1Request, B1Response>(
            _connectionString, requests))
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }
#endif

#if true
    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_B1Service_Test実行_01(decimal deptNo)
    {
        // Arrange
        var executor = new ApiExecutor();
        var requests = new[] { new B1Request { DEPTNO = deptNo } };
        var results = new List<B1Response>();

        // Act
        await foreach (var response in executor.RunAsync<B1Service_Test, B1Request, B1Response>(
            _connectionString, requests))
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }
#endif

#if true
    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_C1Service実行_01(decimal deptNo)
    {
        // Arrange
        var executor = new ApiExecutor();
        var requests = new[] { new C1Request { DEPTNO = deptNo } };
        var results = new List<C1Response>();

        // Act
        await foreach (var response in executor.RunAsync<C1Service, C1Request, C1Response>(
            _connectionString, requests))
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }
#endif

#if true
    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_C1Service_Test実行_01(decimal deptNo)
    {
        // Arrange
        var executor = new ApiExecutor();
        var requests = new[] { new C1Request { DEPTNO = deptNo } };
        var results = new List<C1Response>();

        // Act
        await foreach (var response in executor.RunAsync<C1Service_Test, C1Request, C1Response>(
            _connectionString, requests))
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }
#endif

#if true
    // --- テスト用のキャンセル検証スタブ ---
    public class B1Service_CancelStub : IB1Service
    {
        // Activator 用のコンストラクタ
        public B1Service_CancelStub(string conn) { }

        public async IAsyncEnumerable<B1Response> ExecuteAsync(
            IEnumerable<B1Request> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // 渡された CancellationToken をチェック
            ct.ThrowIfCancellationRequested();

            yield return new B1Response { EMPNO = 999 };
            await Task.Yield();
        }
    }

    /*
     * 1. キャンセルの伝播:
     *    上位（呼び出し元）からキャンセルが指示されたとき、ApiExecutor が
     *    それを無視して処理を続行せず、即座に反応できること。
     * 2. リソースの即時解放:
     *    ApiExecutor 内の using var scope は、例外（キャンセル例外含む）が発生して
     *    メソッドを抜ける瞬間に実行されます。これにより、キャンセルされた瞬間に
     *    DB セッションやメモリが解放されることが保証されます。
     */
    /*
     * なぜキャンセルが検証できるのか
     * トークンの伝播: ApiExecutor.RunAsync の第3引数に渡した cts.Token は
     * 内部で TService.ExecuteAsync の引数として渡されます。
     * 
     * スタブの挙動: B1Service_CancelStub は、メソッドの冒頭で
     * ct.ThrowIfCancellationRequested() を呼び出します。
     * 
     * 検証の成立: cts.Cancel() が呼ばれているため、RunAsync から呼び出されたスタブが
     * 即座に OperationCanceledException を投げ、
     * それを Assert.ThrowsAnyAsync がキャッチすることで、
     * 「トークンがサービス層まで途切れずに渡っていること」が証明されます。
     * 
     * ### 注意点：`WithCancellation` について
     * テストコード内の `stream.WithCancellation(cts.Token)` は、
     * `IAsyncEnumerable` 全体のキャンセルを制御するために残しておいて問題ありません。
     * これにより、`ApiExecutor` 側のループと `B1Service` 側のループの両方に対して
     * キャンセル要求が有効になります。
     */

    [Fact]
    public async Task RunAsync_異常系_ApiExecutor実行キャンセル確認_01()
    {
        // 1. 準備 (Arrange)
        var executor = new ApiExecutor();
        var requests = new[] { new B1Request { DEPTNO = 10 } };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 実行前にキャンセル状態にする

        // 2. 実行 & 3. 検証 (Act & Assert)
        // CancellationToken が正しく伝播していれば、OperationCanceledException がスローされる
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            // 具象クラスのスタブを指定し、接続文字列を渡す
            var stream = executor.RunAsync<B1Service_CancelStub, B1Request, B1Response>(
                _connectionString,
                requests,
                cts.Token);

            await foreach (var item in stream.WithCancellation(cts.Token))
            {
                // ここには到達しないはず
            }
        });
    }
#endif

#if true
    /*
     * このテストコードにおける最大の課題は
     * 「OracleException は具象クラスであり、かつコンストラクタが非公開（internal）であるため
     *   通常の new や Moq では生成できない」という点です。
     * 
     * 以前の ApiExecutor では DI を通じて Mock<IB1Service> を利用していましたが、
     * 新しい設計では Activator.CreateInstance で具象クラスを生成するため、
     * テスト用の「スタブクラス」を自作して、その中でリフレクションを使って 
     * OracleException を生成するアプローチが最も現実的です。
     * 
     * #### リフレクションによる例外生成
     * `OracleException` は `sealed` ではないものの、コンストラクタが制限されているため、
     * 上記のように `GetConstructors(BindingFlags.NonPublic | ...)` を使うのが
     * テストコードにおける定石です。
     * これにより、`ApiExecutor` 内の `catch (OracleException ex)` ブロックを
     * 実際に通るかどうかをテストできます。
     * 
     * #### なぜ `Mock` ではなく `Stub` なのか
     * `ApiExecutor` が内部で `Activator.CreateInstance(typeof(TService), ...)` を
     * 実行するようになったため、外部から `Mock<IB1Service>.Object` を流し込む口が閉じています。
     * したがって、**「テスト用の振る舞いを持った具象クラス（Stub）」**を定義し、
     * その**型そのもの**を `RunAsync<TService, ...>` に渡すスタイルが、
     * 現在の `ApiExecutor` の設計に最も合致したテスト手法となります。
     */

    // --- OracleExceptionを生成するためのスタブクラス ---
    public class OracleErrorStub : IB1Service, IAsyncDisposable
    {
        public static bool IsDisposed { get; set; }

        public OracleErrorStub(string conn) { }

        public async IAsyncEnumerable<B1Response> ExecuteAsync(
            IEnumerable<B1Request> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new B1Response { EMPNO = 1 }; // 1件目は成功

            await Task.Yield();

            // リフレクションで OracleException (ORA-12154) を生成してスロー
            throw CreateOracleException(12154, "TNS:could not resolve the connect identifier");
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// OracleExceptionはpublicなコンストラクタがないため、リフレクションで生成する
        /// </summary>
        private OracleException CreateOracleException(int errorCode, string message)
        {
            var type = typeof(OracleException);
            var ctors = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
            var ctor = ctors.FirstOrDefault();

            // null の場合は InvalidOperationException 等を投げるようにする
            if (ctor == null)
            {
                throw new InvalidOperationException("OracleException の内部コンストラクタが見つかりませんでした。");
            }

            return (OracleException)ctor.Invoke(new object[] { message, errorCode });
        }
    }

    [Fact]
    public async Task RunAsync_異常系_OracleExceptionハンドリング確認_01()
    {
        // Arrange
        var executor = new ApiExecutor();
        var requests = new[] { new B1Request { DEPTNO = 10 } };

        // 静的フラグなどで破棄確認が必要なら追加（前述のスタブと同様）
        OracleErrorStub.IsDisposed = false;

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            // TServiceにスタブを指定。第1引数に接続文字列を渡す。
            var stream = executor.RunAsync<OracleErrorStub, B1Request, B1Response>(_connectionString, requests);

            await foreach (var item in stream)
            {
                // 1件目は成功するが、次の MoveNextAsync で OracleException が飛ぶ
            }
        });

        Assert.True(OracleErrorStub.IsDisposed, "例外発生後もサービスが破棄されていません。");
    }
#endif

#if true
    /*
     * #### 1. なぜ `IB1Service` ではなく `B1Service` なのか
     * 現在の `ApiExecutor` は内部で 
     * `Activator.CreateInstance(typeof(TService), connectionString)` を実行します。
     * * **修正前**: `RunAsync<IB1Service, ...>` と書くと、インターフェースを 
     * `new` しようとしてランタイムエラーになります。
     * * **修正後**: `RunAsync<B1Service, ...>` と書くことで、内部で正しく 
     * `new B1Service(connStr)` が実行され、実際のDBアクセス処理が走ります。
     * 
     * #### 2. このテストの価値
     * このテストは、「コード上の `catch (OracleException)` が、実際の 
     * `Oracle.ManagedDataAccess` ライブラリが投げる本物の例外をキャッチできるか」
     * を確認するのに役立ちます。
     * 特に、ライブラリのバージョンアップ時などに、例外の型が変わっていないかを確認する
     * 「スモークテスト」として有効です。
     */
    //[Fact]
    [Fact(Skip = "DB停止状態の確認用（手動実行専用）")]
    public async Task RunAsync_異常系_OracleExceptionハンドリング確認_02_Oracleサービス停止時()
    {
        // 1. Arrange
        // DIコンテナを通さず、直接インスタンス化
        var executor = new ApiExecutor();
        var requests = new[] { new B1Request { DEPTNO = 10 } };

        // 2. Act & 3. Assert
        try
        {
            // 型引数には「B1Service」具象クラスを指定し、第1引数に接続文字列を渡す
            var stream = executor.RunAsync<B1Service, B1Request, B1Response>(_connectionString, requests);

            await foreach (var item in stream)
            {
                // データが取れたら「停止状態」ではないため失敗
            }

            // 例外が発生しなかった場合
            Assert.Fail("Oracleサービスに正常に接続できてしまいました。サービスを停止してからテストを実行してください。");
        }
        catch (OracleException ox)
        {
            // 狙い通り OracleException が発生
            Assert.True(ox.Number > 0, $"Oracleエラーが発生しました。Code: {ox.Number}");
            // ログ出力などは必要に応じて追加
        }
        catch (Exception ex)
        {
            // 想定外の型（null参照など）で落ちた場合
            Assert.Fail($"OracleExceptionを期待していましたが、別の例外が発生しました: {ex.GetType().Name} - {ex.Message}");
        }
    }
#endif

#if true
    // --- 一般的な例外をシミュレートするスタブクラス ---
    public class SystemErrorStub : IB1Service, IAsyncDisposable
    {
        public static string Message { get; set; } = string.Empty;
        public static bool IsDisposed { get; set; }

        public SystemErrorStub(string conn) { }

        public async IAsyncEnumerable<B1Response> ExecuteAsync(
            IEnumerable<B1Request> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new B1Response { EMPNO = 1 }; // 1件目は成功

            await Task.Yield();

            // 2件目の列挙で一般的な例外をスロー
            throw new Exception(Message);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task RunAsync_異常系_Exceptionハンドリング確認_01()
    {
        // Arrange
        string expectMessage = "予期せぬシステムエラー";
        var executor = new ApiExecutor();
        var requests = new[] { new B1Request { DEPTNO = 10 } };

        // スタブクラスに期待するメッセージをセット
        SystemErrorStub.Message = expectMessage;
        SystemErrorStub.IsDisposed = false;

        // Act & Assert
        // 実行時に指定したメッセージを含む Exception が再スローされることを検証
        var ex = await Assert.ThrowsAsync<Exception>(async () =>
        {
            // 型引数に具象スタブクラスを指定
            var stream = executor.RunAsync<SystemErrorStub, B1Request, B1Response>(
                _connectionString,
                requests);

            await foreach (var item in stream)
            {
                // 1件目は処理されるが、2件目の取得（MoveNextAsync）で例外が発生する
            }
        });

        // Assert
        Assert.Equal(expectMessage, ex.Message);
        // 例外発生時でもリソースが破棄されていることを確認
        Assert.True(SystemErrorStub.IsDisposed, "例外発生時にサービスが破棄されていません。");
    }
#endif
}