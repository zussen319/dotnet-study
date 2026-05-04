using Microsoft.Extensions.DependencyInjection;
using Moq;
using ServiceApi.Requests;
using ServiceApi.Requests.B1;
using ServiceApi.Responses;
using ServiceApi.Responses.B1;
using ServiceApi.Services;
using ServiceApi.Services.B1;

namespace ServiceApi.Tests;

/*
 * このテストを行う最大の理由は、「メモリリークとDBセッションの枯渇を防ぐため」です。
 * 特に ApiExecutor の using var scope = serviceProvider.CreateScope(); 
 * という実装は非常に優れていますが、もし誤って using を消してしまったり、
 * finally の DisposeAsync を忘れたりすると、Oracle のセッションが接続されたまま残り続け、
 * 最終的にデータベースがダウンする原因になります。
 * 
 * 1. 準備すべき 3 つのテスト観点
 * (1) 正常系のライフサイクル完了テスト
 * 検証内容: 全データを読み終わった後、Service インスタンスが Dispose され、
 * DIの Scope が破棄されているか。
 * 重要性: これが失敗すると、DBセッションがサーバーに残リ続け、最終的にDBがハングアップします。
 * 
 * (2) 呼び出し側による「途中中断」テスト
 * 検証内容: 呼び出し元が await foreach を途中で break した場合でも、finally ブロックを通過し、
 * Dispose が呼ばれるか。
 * 重要性: 全件フェッチせずに終了するケース（エラーやユーザーキャンセル）でも安全であることを保証します。
 * 
 * (3) 例外発生時の「再送出とクリーンアップ」テスト
 * 検証内容: MoveNextAsync 中に OracleException 等が発生した際、ログが出力され、
 * 例外が呼び出し元に throw され、かつリソースが解放されるか。
 * 重要性: 異常系での後始末漏れを防ぎます。
 * 
 * 今後の効率化：ジェネリックなテストの検討
 * ApiExecutor のテストが通れば、フレームワークとしての「基盤」は保証されます。
 * 今後の開発では以下の戦略をとるのが効率的です。
 * - 基盤テスト (ApiExecutor): 上記のようなリソース管理のテストを一度だけしっかり書く。
 * - 個別テスト (B1Service, B2Service...): すでに実施された「DB vs JSON」のデータ比較テストを
 * 各サービスごとに作成する。
 * ApiExecutor のコード内にある using var scope = serviceProvider.CreateScope(); は、
 * IAsyncEnumerable の寿命と DIスコープを同期させる非常に優れた実装です。
 * この「設計の正しさ」をテストコードで裏付けておくことは、将来的なリファクタリング
 * （例：ロギング機能の追加など）の際の強力なセーフティネットになります。
 */

// テスト用のスタブ
public class MockResponse : ResponseBase { public int Id { get; set; } }
public class MockRequest : RequestBase { }

public class TEST_ApiExecutor
{
    [Fact]
    public async Task RunAsync_ShouldDisposeService_WhenCompleted()
    {
        // Arrange
        var services = new ServiceCollection();
        bool isDisposed = false;

        /*
         * Moq を導入すると、本来は複雑な準備が必要なインターフェースを
         * 以下のように簡単に偽装（モック化）できるようになります。
         */
        // モックサービスを登録
        // 1. IApiService インターフェースを実装したスタブを作成
        // インターフェースの形をした「身代わり」を作る
        var mockService = new Mock<IApiService<MockRequest, MockResponse>>();

        // IAsyncDisposable か IDisposable を検知するために必要
        // 「ExecuteAsync が呼ばれたら、このダミーデータを返せ」と教え込む
        mockService.As<IDisposable>().Setup(s => s.Dispose()).Callback(() => isDisposed = true);

        async IAsyncEnumerable<MockResponse> FakeStream()
        {
            yield return new MockResponse { Id = 1 };
            yield return new MockResponse { Id = 2 };
            await Task.Yield();
        }

        mockService.Setup(s => s.ExecuteAsync(It.IsAny<MockRequest>()))
                   .Returns(FakeStream());

        // mock.Object を渡せば、本物の IB1Service として振る舞う
        services.AddTransient(_ => mockService.Object);
        var provider = services.BuildServiceProvider();
        var executor = new ApiExecutor(provider);

        // Act
        int count = 0;
        await foreach (var item in executor.RunAsync<IApiService<MockRequest, MockResponse>, MockRequest, MockResponse>(new MockRequest()))
        {
            count++;
        }

        // Assert
        Assert.Equal(2, count);
        // ApiExecutor の scope が抜けたことで、service の Dispose() が呼ばれたか検証
        Assert.True(isDisposed, "処理完了後にサービスが破棄されていません。");
    }

    [Theory]
    [InlineData(0)]      // 0件（データなし）
    [InlineData(1)]      // 最小件数
    [InlineData(100)]    // 中規模
    [InlineData(1000)]   // 大量データ想定
    public async Task RunAsync_ShouldCompleteAndDispose_RegardlessOfCount(int testCount)
    {
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
        // Arrange
        var services = new ServiceCollection();
        bool isServiceDisposed = false;

        // サービスのモック作成
        var mockService = new Mock<IB1Service>();

        // Dispose検知（DIコンテナ経由で破棄されることを確認）
        mockService.As<IDisposable>()
                   .Setup(s => s.Dispose())
                   .Callback(() => isServiceDisposed = true);

        // testCountの分だけデータを生成するローカル関数
        async IAsyncEnumerable<B1Response> FakeStream(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new B1Response { EMPNO = i };
            }
            await Task.Yield();
        }

        // ExecuteAsyncが呼ばれたら指定件数のストリームを返す
        mockService.Setup(s => s.ExecuteAsync(It.IsAny<B1Request>()))
                   .Returns(FakeStream(testCount));

        // ApiExecutorが内部でCreateScope()するため、Scopedで登録
        services.AddScoped(_ => mockService.Object);

        var provider = services.BuildServiceProvider();
        var executor = new ApiExecutor(provider);
        var request = new B1Request { DEPTNO = 10 };

        // Act
        int actualCount = 0;
        await foreach (var item in executor.RunAsync<IB1Service, B1Request, B1Response>(request))
        {
            actualCount++;
        }

        // Assert
        Assert.Equal(testCount, actualCount); // 期待した件数が届いているか
        Assert.True(isServiceDisposed, $"件数 {testCount} において、サービスが正しく破棄されていません。");
    }

    [Fact]
    public async Task RunAsync_ShouldRethrow_WhenExceptionOccurs()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockService = new Mock<IApiService<MockRequest, MockResponse>>();

        async IAsyncEnumerable<MockResponse> ErrorStream()
        {
            yield return new MockResponse { Id = 1 };
            throw new InvalidOperationException("DB接続エラー");
        }

        mockService.Setup(s => s.ExecuteAsync(It.IsAny<MockRequest>()))
                   .Returns(ErrorStream());

        services.AddTransient(_ => mockService.Object);
        var provider = services.BuildServiceProvider();
        var executor = new ApiExecutor(provider);

        // Act & Assert
        // 例外が外側まで伝播することを確認
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in executor.RunAsync<IApiService<MockRequest, MockResponse>, MockRequest, MockResponse>(new MockRequest()))
            {
                // 1件目は取れるが、2件目の取得で例外が飛ぶ
            }
        });
    }
}