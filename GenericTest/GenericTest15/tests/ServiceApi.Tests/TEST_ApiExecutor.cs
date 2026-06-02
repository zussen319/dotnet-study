using Oracle.ManagedDataAccess.Client;
using ServiceApi.Common;
using ServiceApi.Requests;
using ServiceApi.Requests.A1;
using ServiceApi.Requests.B1;
using ServiceApi.Requests.C1;
using ServiceApi.Requests.C2;
using ServiceApi.Responses;
using ServiceApi.Responses.A1;
using ServiceApi.Responses.B1;
using ServiceApi.Responses.C1;
using ServiceApi.Responses.C2;
using ServiceApi.Services;
using ServiceApi.Services.A1;
using ServiceApi.Services.B1;
using ServiceApi.Services.C1;
using ServiceApi.Services.C2;
using ServiceApi.Tests.Common;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ServiceApi.Tests;

// テスト用のスタブ
public record MockResponse : ResponseBase { public int Id { get; set; } }
public record MockRequest : RequestBase { }


public class TEST_ApiExecutor
{
    // DB接続文字列：テスト用設定ファイル（ServiceApi.Test.Json）から取得
    private readonly string _connectionString =
        TEST_ConfigurationManager.GetValue<string>(ConfigId.ConnectionString);

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
    public class ServiceCountStub : TestServiceBase<MockRequest, MockResponse>
    {
        public static bool IsDisposed { get; set; }
        public static int YieldCount { get; set; }

        // コンストラクタ：基底クラスにconnectionStringを渡す（内部では無視される）
        public ServiceCountStub(string connStr, int fetchRows = 0) : base(connStr, fetchRows) { }

        public override async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int i = 0; i < YieldCount; i++)
            {
                yield return new MockResponse { Id = i };
            }
            await Task.Yield();
        }

        // DisposeAsyncをオーバーライドして独自の検証用ロジックを追加
        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            // 基底クラスのDisposeAsyncを呼ぶ
            return base.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(0)]      // 0件（データなし）
    [InlineData(1)]      // 最小件数
    [InlineData(100)]    // 中規模
    [InlineData(1000)]   // 大量データ想定
    public async Task RunAsync_正常系_終了時サービス破棄確認_01(int testCount)
    {
        // Arrange
        // 静的プロパティを初期化
        ServiceCountStub.IsDisposed = false;
        ServiceCountStub.YieldCount = testCount;

        // Act
        int actualCount = 0;
        IAsyncEnumerable<MockResponse> responseStream =
            new ApiExecutor().RunAsync<ServiceCountStub, MockRequest, MockResponse>(
                _connectionString, [new MockRequest()]);
        await foreach (MockResponse response in responseStream) { actualCount++; }

        // Assert
        Assert.Equal(testCount, actualCount);
        Assert.True(ServiceCountStub.IsDisposed,
            $"件数 {testCount} において、サービスが正しく破棄されていません。");
    }

    /* 正常系：リソース破棄（Dispose）の検証（同期・非同期問わず）*/

    // --- 統合版スタブクラス ---// --- 統合版スタブクラス（正常系・異常系共用） ---
    public class DisposableStub : TestServiceBase<MockRequest, MockResponse>
    {
        // インスタンス化されたかどうかを追跡するフラグ
        public static bool IsInstantiated { get; set; }

        public static bool IsDisposed { get; set; }

        private readonly string _connectionString;

        public DisposableStub(string connStr, int fetchRows = 0) : base(connStr, fetchRows)
        {
            _connectionString = connStr;
            // コンストラクタが呼ばれたらtrueにする
            IsInstantiated = true;
        }

        public override async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (_connectionString.Contains("ERROR_TRIGGER=THROW"))
            {
                await Task.Yield();
                throw new InvalidOperationException("Stub Error");
            }
            yield return new MockResponse { Id = 10 };
        }

        public override ValueTask DisposeAsync()
        {
            // リソース破棄を確認
            IsDisposed = true;
            return base.DisposeAsync();
        }
    }

    [Fact]
    public async Task RunAsync_正常系_リソース破棄の検証_01()
    {
        /*
         * 正常終了時、サービスのリソースが破棄されていること
         */
        DisposableStub.IsDisposed = false;

        // 普通の接続文字列を渡す
        IAsyncEnumerable<MockResponse> responseStream =
            new ApiExecutor().RunAsync<DisposableStub, MockRequest, MockResponse>(
                _connectionString, [new MockRequest()]);
        await foreach (MockResponse _ in responseStream) { }

        Assert.True(DisposableStub.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_異常系_リソース破棄の検証_01()
    {
        /*
         * 例外発生時でもサービスのリソースが破棄されていること
         */
        DisposableStub.IsDisposed = false;

        // 接続文字列に "THROW" を含めることで、スタブに例外を投げさせる
        string errorConn = _connectionString + ";ERROR_TRIGGER=THROW";

        IAsyncEnumerable<MockResponse> responseStream =
            new ApiExecutor().RunAsync<DisposableStub, MockRequest, MockResponse>(
                errorConn, [new MockRequest()]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            { await foreach (MockResponse _ in responseStream) { } });

        Assert.True(DisposableStub.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_正常系_リクエスト件数0件の検証_01()
    {
        // Arrange
        // フラグをリセット
        DisposableStub.IsInstantiated = false;
        DisposableStub.IsDisposed = false;

        // Act
        IAsyncEnumerable<MockResponse> responseStream =
            new ApiExecutor().RunAsync<DisposableStub, MockRequest, MockResponse>(
                _connectionString, Enumerable.Empty<MockRequest>()); // 空のリクエスト
        int count = 0;
        await foreach (MockResponse _ in responseStream) { count++; }

        // Assert
        // 1. ループが一度も回っていないこと
        Assert.Equal(0, count);

        // 2. サービスが一度もインスタンス化されていないこと（＝無駄な接続が行われていない）
        Assert.False(DisposableStub.IsInstantiated, "リクエストが空の場合、サービスをインスタンス化すべきではありません。");

        // 3. インスタンスがないので、Disposeも当然呼ばれていないこと
        Assert.False(DisposableStub.IsDisposed, "インスタンス化されていないため、Disposeも呼ばれないはずです。");
    }

    // --- テスト用の異常系スタブクラス ---
    public class ExceptionStub : IApiService<MockRequest, MockResponse>, IAsyncDisposable
    {
        public static bool IsDisposed { get; set; }

        public ExceptionStub(string connStr, int fetchRows = 0) { }

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
        // 静的フラグで例外発生後の破棄を確認
        ExceptionStub.IsDisposed = false;

        // Act & Assert
        IAsyncEnumerable<MockResponse> responseStream =
            new ApiExecutor().RunAsync<ExceptionStub, MockRequest, MockResponse>(
                _connectionString, [new MockRequest()]);

        // 1. 指定した例外が正しく外側まで飛んでくることを検証
        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                // TService には例外を投げる具象クラスを指定
                await foreach (MockResponse response in responseStream)
                {
                    // 1件目は正常に処理され、その後の列挙で例外が発生する
                }
            });

        // メッセージの検証（必要であれば）
        Assert.Equal("DB接続エラー", exception.Message);

        // 2. 重要：例外発生時でも service.DisposeAsync() が呼ばれていることを検証
        Assert.True(ExceptionStub.IsDisposed, "例外発生時にサービスが破棄されていません。");
    }

    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_A1Service実行_01(decimal value)
    {
        // Arrange
        ApiExecutor executor = new();
        IEnumerable<A1Request> requests = [ new A1Request { A1Value = value } ];
        List<A1Response> results = new();

        // Act
        IAsyncEnumerable<A1Response> responseStream =
            executor.RunAsync<A1Service, A1Request, A1Response>(_connectionString, requests);

        await foreach (A1Response response in responseStream)
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_A1Service_Test実行_01(decimal value)
    {
        // Arrange
        ApiExecutor executor = new();
        IEnumerable<A1Request> requests = [new A1Request { A1Value = value }];
        List<A1Response> results = new();

        // Act
        IAsyncEnumerable<A1Response> responseStream =
            executor.RunAsync<A1Service_Test, A1Request, A1Response>(_connectionString, requests);

        await foreach (A1Response response in responseStream)
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(new int[] { 30, 20, 10 })]
    public async Task RunAsync_正常系_B1Service実行_01(int[] deptNos)
    {
        // Arrange
        ApiExecutor executor = new();
        IEnumerable<B1Request> requests =
            deptNos.Select(d => new B1Request { DEPTNO = (decimal)d }).ToList();
        List<B1Response> results = new();

        // Act
        IAsyncEnumerable<B1Response> responseStream =
            executor.RunAsync<B1Service, B1Request, B1Response>(_connectionString, requests);

        await foreach (B1Response response in responseStream)
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_B1Service_Test実行_01(decimal deptNo)
    {
        // Arrange
        ApiExecutor executor = new();
        IEnumerable<B1Request> requests = [ new B1Request { DEPTNO = deptNo } ];
        List<B1Response> results = new();

        // Act
        IAsyncEnumerable<B1Response> responseStream =
            executor.RunAsync<B1Service_Test, B1Request, B1Response>(_connectionString, requests);

        await foreach (B1Response response in responseStream)
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(new int[] { 30, 20, 10 })]
    public async Task RunAsync_正常系_C1Service実行_01(int[] deptNos)
    {
        // Arrange
        ApiExecutor executor = new();
        IEnumerable<C1Request> requests =
            deptNos.Select(d => new C1Request { DEPTNO = (decimal)d }).ToList();
        List<C1Response> results = new();

        // Act
        IAsyncEnumerable<C1Response> responseStream =
            executor.RunAsync<C1Service, C1Request, C1Response>(_connectionString, requests);

        await foreach (C1Response response in responseStream)
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_C1Service_Test実行_01(decimal deptNo)
    {
        // Arrange
        ApiExecutor executor = new();
        IEnumerable<C1Request> requests = [ new C1Request { DEPTNO = deptNo } ];
        List<C1Response> results = new();

        // Act
        IAsyncEnumerable<C1Response> responseStream =
            executor.RunAsync<C1Service_Test, C1Request, C1Response>(_connectionString, requests);

        await foreach (C1Response response in responseStream)
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(new int[] { 30, 20, 10 })]
    public async Task RunAsync_正常系_C2Service実行_01(int[] deptNos)
    {
        // Arrange
        ApiExecutor executor = new();
        IEnumerable<C2Request> requests =
            deptNos.Select(d => new C2Request { DEPTNO = (decimal)d }).ToList();
        List<C2Response> results = new();

        // Act
        IAsyncEnumerable<C2Response> responseStream =
            executor.RunAsync<C2Service, C2Request, C2Response>(_connectionString, requests);

        await foreach (C2Response response in responseStream)
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_C2Service_Test実行_01(decimal deptNo)
    {
        // Arrange
        ApiExecutor executor = new();
        IEnumerable<C2Request> requests = [ new C2Request { DEPTNO = deptNo } ];
        List<C2Response> results = new();

        // Act
        IAsyncEnumerable<C2Response> responseStream =
            executor.RunAsync<C2Service_Test, C2Request, C2Response>(_connectionString, requests);

        await foreach (C2Response response in responseStream)
        {
            results.Add(response);
        }

        // Assert
        Assert.NotEmpty(results);
    }

    // --- テスト用のキャンセル検証スタブ ---
#if true
    public class ServiceCancelStub : TestServiceBase<MockRequest, MockResponse> {
        // キャンセル発生時に破棄されたことを追跡するフラグ
        public static bool IsDisposed { get; set; }

        // 基底クラスのコンストラクタ(string _)を呼び出す
        public ServiceCancelStub(string connStr, int fetchRows = 0) : base(connStr, fetchRows) { }

        public override async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // 渡された CancellationToken をチェック
            ct.ThrowIfCancellationRequested();

            yield return new MockResponse { Id = 999 };
            await Task.Yield();
        }

        // 破棄を検証するため override
        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }
    }
#else
    public class ServiceCancelStub : TestServiceBase<MockRequest, MockResponse>
    {
        // 基底クラスのコンストラクタ(string _)を呼び出す
        public ServiceCancelStub(string connStr, int fetchRows = 0) : base(connStr, fetchRows) { }

        public override async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // 渡された CancellationToken をチェック
            ct.ThrowIfCancellationRequested();

            yield return new MockResponse { Id = 999 };
            await Task.Yield();
        }
    }
#endif

    /*
     * 1. キャンセルの伝播:
     *    上位（呼び出し元）からキャンセルが指示されたとき、ApiExecutor が
     *    それを無視して処理を続行せず、即座に反応できること。
     * 2. リソースの即時解放:
     *    ApiExecutor 内の await using (service) は、例外（キャンセル例外含む）が発生して
     *    メソッドを抜ける瞬間に DisposeAsync を呼び出します。これにより、キャンセルされた
     *    瞬間にサービス（本番では DB セッション）が解放されることが保証されます。
     *    （本テストでは ServiceCancelStub.IsDisposed で実際に破棄を検証している）
     */
    /*
     * なぜキャンセルが検証できるのか
     * トークンの伝播: ApiExecutor.RunAsync の第3引数に渡した cts.Token は
     * 内部で TService.ExecuteAsync の引数として渡されます。
     * 
     * スタブの挙動: ServiceCancelStub は、メソッドの冒頭で
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
     * これにより、`ApiExecutor` 側のループとサービス（ServiceCancelStub）側のループの
     * 両方に対してキャンセル要求が有効になります。
     */
#if true
    [Fact]
    public async Task RunAsync_異常系_ApiExecutor実行キャンセル確認_01()
    {
        // 1. 準備 (Arrange)
        ServiceCancelStub.IsDisposed = false;
        using CancellationTokenSource cts = new();
        cts.Cancel(); // 実行前にキャンセル状態にする

        // 2. 実行 & 3. 検証 (Act & Assert)

        // CancellationToken が正しく伝播していれば、OperationCanceledException がスローされる
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            // 具象クラスのスタブを指定し、接続文字列を渡す
            IAsyncEnumerable<MockResponse> stream =
                new ApiExecutor().RunAsync<ServiceCancelStub, MockRequest, MockResponse>(
                    _connectionString, [new MockRequest()], cts.Token);

            await foreach (MockResponse item in stream.WithCancellation(cts.Token)) {
                // ここには到達しないはず
            }
        });

        // キャンセル時も await using によりサービスが破棄されること（＝即時解放の実証）
        Assert.True(ServiceCancelStub.IsDisposed, "キャンセル発生時にサービスが破棄されていません。");
    }
#else
    [Fact]
    public async Task RunAsync_異常系_ApiExecutor実行キャンセル確認_01()
    {
        // 1. 準備 (Arrange)
        using CancellationTokenSource cts = new();
        cts.Cancel(); // 実行前にキャンセル状態にする

        // 2. 実行 & 3. 検証 (Act & Assert)

        // CancellationToken が正しく伝播していれば、OperationCanceledException がスローされる
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            // 具象クラスのスタブを指定し、接続文字列を渡す
            IAsyncEnumerable<MockResponse> stream =
                new ApiExecutor().RunAsync<ServiceCancelStub, MockRequest, MockResponse>(
                    _connectionString, [new MockRequest()], cts.Token);

            await foreach (MockResponse item in stream.WithCancellation(cts.Token))
            {
                // ここには到達しないはず
            }
        });
    }
#endif

    /*
     * 処理の途中でタイムアウトが発生した場合のテスト
     */
    /*
     * 非同期メソッドへの伝播確認:
     * スタブ内の await Task.Delay(..., ct) にトークンを渡しています。
     * これにより、ApiExecutor からサービス層へ正しくトークンが渡り、
     * 「重い非同期処理（DBクエリなど）」が途中でキャンセル可能であることを証明できます。
     * 
     * リソースの早期解放:
     * タイムアウト時に即座に例外が発生すれば、await using によるリソース破棄も
     * 即座に実行されます。これを検証することで、システム全体の安定性を確認できます。
     */
#if true
    public class ServiceTimeoutStub : TestServiceBase<MockRequest, MockResponse> {
        // タイムアウト発生時に破棄されたことを追跡するフラグ
        public static bool IsDisposed { get; set; }

        // 基底クラスのコンストラクタを呼び出す
        public ServiceTimeoutStub(string dummyStr, int dummyRows = 0) : base(dummyStr, dummyRows) { }

        // override を追加
        public override async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // 1件目はすぐに返す
            yield return new MockResponse { Id = 1 };

            // 2件目を出す前に、非常に長い時間がかかる処理をシミュレート
            // この間にタイムアウトキャンセルが発生する想定
            await Task.Delay(10000, ct);

            yield return new MockResponse { Id = 2 };
        }

        // 破棄を検証するため override
        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }
    }
#else
    public class ServiceTimeoutStub : TestServiceBase<MockRequest, MockResponse>
    {
        // 基底クラスのコンストラクタを呼び出す
        public ServiceTimeoutStub(string dummyStr, int dummyRows = 0) : base(dummyStr, dummyRows) { }

        // override を追加
        public override async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            // 1件目はすぐに返す
            yield return new MockResponse { Id = 1 };

            // 2件目を出す前に、非常に長い時間がかかる処理をシミュレート
            // この間にタイムアウトキャンセルが発生する想定
            await Task.Delay(10000, ct);

            yield return new MockResponse { Id = 2 };
        }
    }
#endif

#if true
    [Fact]
    public async Task RunAsync_異常系_ApiExecutor実行中のタイムアウトキャンセル確認_01()
    {
        // Arrange
        ServiceTimeoutStub.IsDisposed = false;
        // 100ミリ秒後に自動的にキャンセル（タイムアウト）される設定
        using CancellationTokenSource cts = new();
        cts.CancelAfter(100);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            IAsyncEnumerable<MockResponse> stream =
                new ApiExecutor().RunAsync<ServiceTimeoutStub, MockRequest, MockResponse>(
                    _connectionString, [new MockRequest()], cts.Token);

            await foreach (MockResponse item in stream.WithCancellation(cts.Token)) {
                // 1件目は受け取れるかもしれないが、2件目の Delay で例外が発生する
            }
        });

        // タイムアウト時も await using によりサービスが破棄されること（＝即時解放の実証）
        Assert.True(ServiceTimeoutStub.IsDisposed, "タイムアウト発生時にサービスが破棄されていません。");
    }
#else
    [Fact]
    public async Task RunAsync_異常系_ApiExecutor実行中のタイムアウトキャンセル確認_01()
    {
        // Arrange
        // 100ミリ秒後に自動的にキャンセル（タイムアウト）される設定
        using CancellationTokenSource cts = new();
        cts.CancelAfter(100);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            IAsyncEnumerable<MockResponse> stream =
                new ApiExecutor().RunAsync<ServiceTimeoutStub, MockRequest, MockResponse>(
                    _connectionString, [new MockRequest()], cts.Token);

            await foreach (MockResponse item in stream.WithCancellation(cts.Token))
            {
                // 1件目は受け取れるかもしれないが、2件目の Delay で例外が発生する
            }
        });
    }
#endif

    /*
     * コンストラクタで受け取った fetchRows を記録するスタブを用意し
     * (a) 既定 100
     * (b) fetchRows: 名前付き指定の伝播
     * (c) ctのみ位置指定で呼べる
     * を確認するテストを追加する
     */
    // --- コンストラクタに渡された fetchRows を捕捉するスタブ ---
    public class FetchRowsCaptureStub : TestServiceBase<MockRequest, MockResponse> {
        // ApiExecutor → Activator 経由でコンストラクタに渡された fetchRows を記録する
        public static int CapturedFetchRows { get; set; }

        // 第2引数 fetchRows を捕捉（基底 TestServiceBase は値を検証せず無視する）
        public FetchRowsCaptureStub(string connStr, int fetchRows = 0) : base(connStr, fetchRows)
        {
            CapturedFetchRows = fetchRows;
        }

        public override async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new MockResponse { Id = 1 };
            await Task.Yield();
        }
    }

    [Fact]
    public async Task RunAsync_正常系_fetchRows既定値確認_01()
    {
        // 引数を何も足さない素の呼び出しで既定値が渡ること

        // Arrange
        FetchRowsCaptureStub.CapturedFetchRows = 0;

        // Act
        // fetchRows を指定せずに呼び出す → ApiConstants.DefaultFetchRows が渡るはず
        IAsyncEnumerable<MockResponse> stream =
            new ApiExecutor().RunAsync<FetchRowsCaptureStub, MockRequest, MockResponse>(
                _connectionString, [new MockRequest()]);
        await foreach (MockResponse _ in stream) { }

        // Assert
        Assert.Equal(ApiConstants.DefaultFetchRows, FetchRowsCaptureStub.CapturedFetchRows);
    }

    [Fact]
    public async Task RunAsync_正常系_fetchRows明示指定確認_01()
    {
        // Arrange
        FetchRowsCaptureStub.CapturedFetchRows = 0;
        const int expected = 500;

        // Act
        // fetchRows を名前付きで明示指定（ct は省略）
        IAsyncEnumerable<MockResponse> stream =
            new ApiExecutor().RunAsync<FetchRowsCaptureStub, MockRequest, MockResponse>(
                _connectionString, [new MockRequest()], fetchRows: expected);
        await foreach (MockResponse _ in stream) { }

        // Assert
        Assert.Equal(expected, FetchRowsCaptureStub.CapturedFetchRows);
    }

    [Fact]
    public async Task RunAsync_正常系_ct位置指定_fetchRows既定値確認_01()
    {
        // ctを位置指定で渡してもfetchRowsを巻き込まないこと
        // Arrange
        FetchRowsCaptureStub.CapturedFetchRows = 0;
        using CancellationTokenSource cts = new(); // キャンセルはしない

        // Act
        /*
         * ct を第3引数（位置指定）で渡し、fetchRows は省略する。
         * シグネチャが (..., CancellationToken ct, int fetchRows) の順（src review-notes §7）
         * であることのコンパイル時／実行時の回帰ガード。
         * もし ct と fetchRows の順序が逆だと、CancellationToken を int 引数に渡せず
         * コンパイルエラーになる（＝引数順を破壊する変更を検知できる）。
         */
        IAsyncEnumerable<MockResponse> stream =
            new ApiExecutor().RunAsync<FetchRowsCaptureStub, MockRequest, MockResponse>(
                _connectionString, [new MockRequest()], cts.Token);
        await foreach (MockResponse _ in stream) { }

        // Assert
        // ctを渡してもfetchRowsは既定値(100)のまま据え置かれること
        Assert.Equal(ApiConstants.DefaultFetchRows, FetchRowsCaptureStub.CapturedFetchRows);
    }

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
    public class OracleErrorStub : TestServiceBase<MockRequest, MockResponse>
    {
        public static bool IsDisposed { get; set; }

        // 基底クラスのコンストラクタ(string _)に引数を渡す
        public OracleErrorStub(string connStr, int fetchRows = 0) : base(connStr, fetchRows) { }

        public override async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new MockResponse(); // 1件目は成功

            await Task.Yield();

            // 2件目で OracleException (ORA-12154) をスロー
            throw CreateOracleException(12154, "TNS:could not resolve the connect identifier");
        }

        // 破棄されたことを確認するために override
        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }

        /// <summary>
        /// OracleExceptionはpublicなコンストラクタがないため、リフレクションで生成する
        /// </summary>
#if true
        private OracleException CreateOracleException(int errorCode, string message)
        {
            Type type = typeof(OracleException);

            // 引数の型シグネチャを明示して内部コンストラクタを特定する
            /*
             * OracleException は sealed かつ全コンストラクタが internal。
             * 利用するのは ODP.NET Managed 23.x で確認した次の5引数版：
             *   (int errCode, string dataSrc, string procedure, string errMsg, int parseErrorOffset)
             * GetConstructors().FirstOrDefault() のように先頭を無条件に選ぶと、
             * 引数個数の異なるコンストラクタを掴み TargetParameterCountException になるため、
             * GetConstructor(types: ...) で型まで指定して取得する。
             */
            ConstructorInfo? ctor = type.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: [typeof(int), typeof(string), typeof(string), typeof(string), typeof(int)],
                modifiers: null);

            if (ctor is null) {
                throw new InvalidOperationException(
                    "OracleException の内部コンストラクタ (int,string,string,string,int) が見つかりませんでした。");
            }

            // errCode=errorCode, dataSrc=null, procedure=null, errMsg=message, parseErrorOffset=0
            return (OracleException)ctor.Invoke([errorCode, null, null, message, 0]);
        }
#else
        private OracleException CreateOracleException(int errorCode, string message)
        {
            Type type = typeof(OracleException);
            // BindingFlags を使うため、using System.Reflection; が必要
            ConstructorInfo[] ctors = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
            ConstructorInfo? ctor = ctors.FirstOrDefault();

            if (ctor is null)
            {
                throw new InvalidOperationException(
                    "OracleException の内部コンストラクタが見つかりませんでした。");
            }
            return (OracleException)ctor.Invoke([message, errorCode]);
        }
#endif
    }

#if true
    [Fact]
    public async Task RunAsync_異常系_OracleExceptionハンドリング確認_01()
    {
        // Arrange
        // 静的フラグなどで破棄確認が必要なら追加
        OracleErrorStub.IsDisposed = false;

        // Act & Assert
        // OracleException「そのもの」がスローされることを検証する。
        /*
         * ThrowsAnyAsync<Exception> ではリフレクションによる OracleException 生成に失敗して
         * 別の例外（InvalidOperationException 等）が飛んでもパスしてしまい、
         * ApiExecutor の catch (OracleException) 分岐を通ったことを保証できない。
         * 型を OracleException に限定し、エラーコードまで確認することで、
         * 「Oracle 専用分岐を実際に通った」ことを担保する。
         */
        OracleException ex =
            await Assert.ThrowsAsync<OracleException>(async () => {
                // TServiceにスタブを指定。第1引数に接続文字列を渡す。
                IAsyncEnumerable<MockResponse> stream =
                    new ApiExecutor().RunAsync<OracleErrorStub, MockRequest, MockResponse>(
                        _connectionString, [new MockRequest()]);

                await foreach (MockResponse item in stream) {
                    // 1件目は成功するが、次の MoveNextAsync で OracleException が飛ぶ
                }
            });

        // スタブが生成した ORA-12154 が伝播していることを確認
        Assert.Equal(12154, ex.Number);

        // 例外発生後もサービスが破棄されていること
        Assert.True(OracleErrorStub.IsDisposed, "例外発生後もサービスが破棄されていません。");
    }
#else
    [Fact]
    public async Task RunAsync_異常系_OracleExceptionハンドリング確認_01()
    {
        // Arrange
        // 静的フラグなどで破棄確認が必要なら追加
        OracleErrorStub.IsDisposed = false;

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            // TServiceにスタブを指定。第1引数に接続文字列を渡す。
            IAsyncEnumerable<MockResponse> stream =
                new ApiExecutor().RunAsync<OracleErrorStub, MockRequest, MockResponse>(
                    _connectionString, [new MockRequest()]);

            await foreach (MockResponse item in stream)
            {
                // 1件目は成功するが、次の MoveNextAsync で OracleException が飛ぶ
            }
        });

        Assert.True(OracleErrorStub.IsDisposed, "例外発生後もサービスが破棄されていません。");
    }
#endif

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

        // 2. Act & 3. Assert
        try
        {
            // 型引数には「B1Service」具象クラスを指定し、第1引数に接続文字列を渡す
            IAsyncEnumerable<B1Response> stream =
                new ApiExecutor().RunAsync<B1Service, B1Request, B1Response>(
                    _connectionString, [new B1Request { DEPTNO = 10 }]);

            await foreach (B1Response item in stream)
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

    // --- 一般的な例外をシミュレートするスタブクラス ---
    public class SystemErrorStub : TestServiceBase<MockRequest, MockResponse>
    {
        public static string Message { get; set; } = string.Empty;
        public static bool IsDisposed { get; set; }

        // 基底クラスのコンストラクタに引数を渡す（内部で無視される）
        public SystemErrorStub(string connStr, int fetchRows = 0) : base(connStr, fetchRows) { }

        public override async IAsyncEnumerable<MockResponse> ExecuteAsync(
            IEnumerable<MockRequest> requests,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new MockResponse(); // 1件目は成功

            await Task.Yield();

            // 2件目の列挙で一般的な例外をスロー
            throw new Exception(Message);
        }

        // 破棄の検証が必要なため override
        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }
    }

    [Fact]
    public async Task RunAsync_異常系_Exceptionハンドリング確認_01()
    {
        // Arrange
        string expectMessage = "予期せぬシステムエラー";

        // スタブクラスに期待するメッセージをセット
        SystemErrorStub.Message = expectMessage;
        SystemErrorStub.IsDisposed = false;

        // Act & Assert
        // 実行時に指定したメッセージを含む Exception が再スローされることを検証
        Exception ex = await Assert.ThrowsAsync<Exception>(async () =>
        {
            // 型引数に具象スタブクラスを指定
            IAsyncEnumerable<MockResponse> stream =
                new ApiExecutor().RunAsync<SystemErrorStub, MockRequest, MockResponse>(
                    _connectionString, [new MockRequest()]);

            await foreach (MockResponse item in stream)
            {
                // 1件目は処理されるが、2件目の取得（MoveNextAsync）で例外が発生する
            }
        });

        // Assert
        Assert.Equal(expectMessage, ex.Message);
        // 例外発生時でもリソースが破棄されていることを確認
        Assert.True(SystemErrorStub.IsDisposed, "例外発生時にサービスが破棄されていません。");
    }
}
