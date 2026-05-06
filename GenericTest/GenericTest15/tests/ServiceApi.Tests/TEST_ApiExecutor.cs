using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests;
using ServiceApi.Requests.B1;
using ServiceApi.Responses;
using ServiceApi.Responses.B1;
using ServiceApi.Services;
using ServiceApi.Services.B1;
using ServiceApi.Tests.Common;
using System.Runtime.CompilerServices;

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
    // DB接続文字列：テスト用設定ファイル（ServiceApi.Test.Json）から取得
    private readonly string _connectionString =
        TEST_ConfigurationManager.GetValue<string>(ConfigId.ConnectionString);

    [Fact]
    public async Task RunAsync_正常系_終了時サービス破棄確認_01()
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
    public async Task RunAsync_正常系_終了時サービス破棄確認_02(int testCount)
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
    public async Task RunAsync_異常系_呼出し元に例外伝播_01()
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

    [Fact]
    public async Task RunAsync_異常系_例外発生時DIスコープ破棄_01()
    {
        /*
         * ApiExecutor 内の using var scope が、例外発生時（catch を通った後）でも
         * 確実に Dispose を呼び出していることを確認する
         */
        /*
         * このテストの注目ポイント
         * Assert.ThrowsAsync:
         * ApiExecutor が内部で例外を握りつぶさず、呼び出し元に正しく伝えているかを
         * チェックしています。
         * 
         * scopeMock.Verify(..., Times.Once):
         * これが今回の肝です。finally ブロックが機能していれば、途中で処理が吹き飛んでも
         * 必ず Dispose() が呼ばれるはずです。
         * 
         * DIスコープの身代わり:
         * IServiceScopeFactory から順にモックをセットアップすることで、
         * using var scope = ... の動きを完全に再現しています。
         */
        /*
         * 1. Assert.ThrowsAsync の正当性
         * ApiExecutor の実装を見ると、内部で catch (Exception ex) を行い、
         * ログを出力した後に throw; しています。
         * テストの価値: もし誤って throw; を書き忘れたり、例外を握りつぶしたり
         * するようにコードを変更してしまった場合、このテストが即座に失敗します。
         * 安心感: 呼び出し元（Program.cs など）がエラーを検知してプロセスを終了させたり、
         * リトライしたりする「連鎖」が壊れていないことを保証しています。
         * 
         * 2. scopeMock.Verify の重要性
         * ApiExecutor では using var scope = ... を使用しています。
         * テストの価値: 非同期ストリーム（yield return）を扱うコードは、
         * 例外が起きた時に「どこまで実行されて、どこを通らないか」が複雑になりがちです。
         * 安心感: finally や using によるクリーンアップが、たとえ DB 接続エラーや
         * システム例外が起きても、確実にリソースを解放することを数値（Times.Once）で証明しています。
         * これは本番環境での接続プール枯渇（リーク）を防ぐための最強の盾です。
         * 
         * 3. DIスコープの再現
         * serviceProvider.CreateScope() という一見シンプルな1行をモックするのは
         * 少し手間がかかりますが、ここを正しく記述したことでテストの信頼性が高まっています。
         * テストの価値: 実際の .NET 実行時と同じ
         * 「Provider -> Factory -> Scope -> Provider -> Service」
         * という解決の鎖をシミュレートできています。
         * 安心感: ApiExecutor が「DIコンテナの仕組みを正しく利用して、
         * スコープ付きサービスを取り出しているか」というDIの作法そのものがテストされています。
         */
        // 1. Setup: モックの準備
        /*
         * DI（依存性の注入）を利用したクラスをテストする際の、典型的な決まり文句
         * (1) serviceProviderMock (外側の親)
         *     アプリ全体のサービス提供者。CreateScope() という「新しい箱（スコープ）」を作る役割。
         * (2) scopeMock (中間の箱)
         *     CreateScope() を呼んだときに返される「一時的な境界線」。
         *    「使い終わったら破棄される（Dispose）」という今回のテストの主役。
         * (3) scopeServiceProviderMock (内側の担当者)
         *     スコープ（箱）の中にいる専用のサービス提供者。
         *     ここから本命の B1Service を取り出す。
         *     
         * // 実装コードのこの動きを...
         * using (var scope = serviceProvider.CreateScope()) // ← (1),(2)が必要
         * {
         *     var service = scope.ServiceProvider.GetService<B1Service>(); // ←(3)が必要
         * }
         */
        var serviceProviderMock = new Mock<IServiceProvider>();
        var scopeMock = new Mock<IServiceScope>();
        var scopeServiceProviderMock = new Mock<IServiceProvider>();

        // --- DIの連鎖を定義 ---

        // serviceProvider.CreateScope() が呼ばれたら scopeMock を返す
        // ※CreateScope は拡張メソッドなので、内部で呼ばれる IServiceScopeFactory をモックする
        /*
         * 「serviceProvider.CreateScope() という魔法の一行が、内部でどう動いているかを
         * 再現している」処理です。
         * .CreateScope() というメソッドは IServiceProvider に直接備わっているものではなく、
         * 「拡張メソッド」という便利ツールです。
         * この拡張メソッドの中身を覗くと、実は裏側で次のような泥臭いことをしています。
         * serviceProvider に対して、「スコープを作る専門家（IServiceScopeFactory）を貸して」と頼む。
         * その専門家（Factory）の CreateScope() メソッドを呼び出す。
         * Moqは「拡張メソッド（CreateScope）」を直接 Setup することができません。
         * そのため、拡張メソッドが裏で呼んでいる「本物の処理」を一つずつモックで
         * 組み立ててあげる必要があるのです。
         */
        /*
         * (1) スコープを作る専門家（Factory）のモック作成
         *    スコープ（箱）を製造する工場」の身代わりを用意します。
         * 
         * (2) 「工場を貸して」と言われた時の設定
         *     ApiExecutor が内部で CreateScope() を呼ぶと、裏側で
         *     「IServiceScopeFactory をください」というリクエストが走ります。
         *     その時に、先ほど作った「偽の工場」を渡すように約束しています。
         * 
         * (3) 「工場でスコープを作って」と言われた時の設定
         *     「偽の工場」に対して、「スコープを作って！」という注文が入ったら、
         *     あらかじめ準備しておいた「偽のスコープ（scopeMock）」を完成品として出すように教えています。
         * 
         */
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        serviceProviderMock
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactoryMock.Object);

        scopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(scopeMock.Object);

        /*
         * 「新しく作ったスコープ（箱）の中から、本命のサービスを取り出せるようにする」
         * という最終的な接続作業をしています。
         * これまでの設定で「箱（スコープ）」までは作れましたが、
         * その箱の中に「中身」を詰め込む作業がここにあたります。
         * 
         * (1) スコープと、その中の「案内役」を紐付ける
         *     IServiceScope（箱）には、必ず ServiceProvider（案内役）が一人付いています。
         *     この行では、「スコープの中にある ServiceProvider を見ろ」と言われたら、
         *     あらかじめ準備しておいた 「スコープ専用の案内役（scopeServiceProviderMock）」 
         *     を出すように設定しています。
         * 
         * (2) 「案内役」に、本命のサービスを渡すよう命じる
         *     「スコープ専用の案内役」に対して、「B1Service_Test をください」というリクエストが来たら、
         *     一番最初に作った 「身代わりのサービス（serviceMock）」 を渡すように約束させています。
         */
        /*
         * なぜこの「二段構え」が必要なのか？
         *   .NETのDI（依存性の注入）には大事なルールがあります。
         *   外側の Provider: アプリ全体でずっと生きている。
         *   スコープ内の Provider: そのスコープ（usingの中）だけで生きている。
         *   ApiExecutor は行儀よく「スコープの中からサービスを取り出す」というコードを書いているため、
         *   テスト側も 「スコープ専用の案内役（Provider）」をわざわざ用意して、
         *   そこからサービスが出てくる という手順を踏まないと、
         *   NullReferenceException になったり、本物のDIの動きをシミュレートできなかったりするのです。
         */
        // scope.ServiceProvider が呼ばれたら、その内部用Providerを返す
        scopeMock.Setup(x => x.ServiceProvider).Returns(scopeServiceProviderMock.Object);

        // Scope内部のProvider
        //var serviceMock = new Mock<B1Service>();
        // 引数にダミーの接続文字列を渡してモックを作成する
        /*
         * Moqがクラス（インターフェースではなく）をモックする場合、
         * そのクラスを継承した「身代わりクラス」を動的に生成します。
         * インターフェースの場合: Mock<IB1Service> と書けば、
         * 中身が空っぽの身代わりを勝手に作れます。
         * クラスの場合: Mock<B1Service> と書くと、そのクラスのコンストラクタを
         * 呼び出す必要があります。
         * 引数があるコンストラクタしか定義されていない場合、
         * new Mock<B1Service>(引数) と明示しないと、Moqはインスタンス化に失敗してしまいます。
         */
        /*
         * モックのインスタンス化 (Arrange - 準備)
         * まず、身代わりとなるオブジェクトを作成します。
         */
        var serviceMock = new Mock<B1Service_Test>("dummy");

        // scope内部のProviderから B1Service を取得できるようにする
        scopeServiceProviderMock
            .Setup(x => x.GetService(typeof(B1Service_Test)))
            .Returns(serviceMock.Object);

        // 2. 異常系の設定: サービスが呼び出されたら例外を投げるようにする
        // ExecuteAsync 自体は IAsyncEnumerable を返すので、
        // ここでは yield return の代わりに例外を投げるヘルパーを定義するか、単純に例外を投げます
        /*
         * [2] 振る舞いの設定 (Arrange - 準備)
         * 「〇〇というメソッドが呼ばれたら、△△を返す（または例外を投げる）」
         * というルールを教え込みます。
         *   It.IsAny() (なんでもいいよ)
         *   「特定の引数」ではなく、「どんなデータが渡されてもこの動きをしてほしい」という時に使います。
         *   今回の It.IsAny<B1Request>() がこれにあたります。
         */
        /*
         * この部分は、これまでの「複雑な入れ子構造（DIの準備）」を経て、ついにたどり着いた
         * 「本番の処理をどう動かすか」を決めているメインの台本です。
         * 
         * (1) serviceMock.Setup(x => x.ExecuteAsync(...))
         *     「もし、ExecuteAsync というメソッドが呼ばれたら……」という条件を指定しています。
         *
         * (2) It.IsAny<B1Request>()
         *     「引数として渡される B1Request は、どんな中身（DEPTNOが10でも20でも）であっても……」
         *     という、条件を広げる指定です。
         *
         * (3).Throws(new InvalidOperationException(...))
         *     「本当の処理（JSON読み込みやDB接続）は一切せずに、即座にこの例外を投げ飛ばせ！」
         *     と命じています。
         * 
         * なぜわざわざ「例外を投げる」ように決めるのか？
         *     このテストの目的を思い出してみると、この設定の重要性が見えてきます。
         *     テストしたいこと： ApiExecutor が、実行中に予期せぬエラーが起きても
         *     「後片付け（Dispose）」を忘れないかどうか。
         *     モックの役割： 本物のエラーが起きるのを待つのではなく、モックを使って
         *     人工的にエラーを発生させること。
         *     もしこの設定を Throws ではなく .Returns(...)（正常なデータを返す）
         *     にしてしまうと、ApiExecutor の catch ブロックや finally ブロックが
         *     「異常時に正しく動くか」をテストすることができなくなります。
         *     
         *   // ApiExecutor.cs の中
         *   var enumerator = service.ExecuteAsync(request).GetAsyncEnumerator();
         *   try {
         *       // ここで enumerator.MoveNextAsync() などが呼ばれた瞬間、
         *       // モックが「あ、台本通りに例外を投げなきゃ！」と反応します。
         *   }
         *   catch (Exception ex) {
         *       // モックが投げた「予期せぬエラー」がここに飛んできます。
         *   }
         *   finally {
         *       // ★ここが今回のテストの主役！
         *       // 例外が起きても、ここを通って scope.Dispose() が呼ばれるかを確認します。
         *   }
         */
        serviceMock
            .Setup(x => x.ExecuteAsync(It.IsAny<B1Request>()))
            .Throws(new InvalidOperationException("予期せぬエラー"));

        // テスト対象のインスタンス化 (ServiceProviderを渡す)
        /*
         * [3] テスト対象への注入と実行 (Act - 実行)
         * 作成したモックを、テストしたいクラス（ApiExecutor）に渡して実行します。
         *   ポイント: モックそのもの（serviceMock）ではなく、
         *   .Object プロパティを渡すのがMoqのルールです。
         *   
         *   .Object (本物として扱う)
         *   Moqのツールキット（Mock<T>）から、実際のクラスのふりをした
         *   インスタンスを取り出す魔法の言葉です。
         */
        /*
         * なぜ ApiExecutor に serviceProviderMock.Object を渡すのか？
         *   ApiExecutor のコンストラクタ（定義）は、以下のようになっていますよね。
         *   public ApiExecutor(IServiceProvider serviceProvider)
         *   テスト対象である ApiExecutor をインスタンス化するには、どうしても
         *   IServiceProvider が必要です。
         *   本番環境では： .NETのシステムが、本物の ServiceProvider を自動で渡してくれます。
         *   テスト環境では： 私たちが手動で new する必要があるため、今まで苦労して組み立ててきた
         *   「身代わり連鎖の出発点」である serviceProviderMock.Object を渡してあげます。
         *   これによって、ApiExecutor が内部で serviceProvider.CreateScope() を呼んだ瞬間に、
         *   あらかじめ設定しておいたモックの連鎖がスタートする仕掛けになっています。
         */
        var executor = new ApiExecutor(serviceProviderMock.Object);

        // 3. Execution & Assertion: 例外が外まで伝播することを確認
        // 第1引数にダミーのRequestオブジェクトを渡します
        /*
         * var request の準備
         *   RunAsync メソッドを呼び出すには、引数としてリクエストオブジェクトが必要です。
         *   これは「実行するためのチケット」のようなものです。
         *   今回のテストの目的は「エラーが起きた時の後片付け」なので、
         *   DEPTNO が 10 でも 99 でもテストの結果（成功/失敗）には直接影響しませんが、
         *   「メソッドを動かすための最小限のルール」として、有効なリクエストオブジェクトを準備しています。
         */
        var request = new B1Request { DEPTNO = 10 };

        // IAsyncEnumerable なので、列挙を開始した瞬間に例外が発生することを検証
        /*
         * [4] 検証 (Assert - 検証)
         * 期待通りの結果になったかを確認します。
         * モック特有の検証として、「正しく呼ばれたか」のチェックもここで行います。
         */
        /*
         * Assert.ThrowsAsync の中身で起きていること
         * 準備が整い、いよいよ以下の「実行」に入ります。
         * この await foreach が始まった瞬間に、ApiExecutor の内部で以下のことが一気に起きます。
         * (1) 注入された serviceProviderMock から scopeMock を作る。
         * (2) scopeMock の中のサービス (serviceMock) を取り出す。
         * (3) serviceMock.ExecuteAsync を呼び出す。
         * (4) モックの設定に従い、即座に InvalidOperationException が投げられる！
         */
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in executor.RunAsync<B1Service_Test, B1Request, B1Response>(request))
            {
                // ここには到達しないはず
            }
        });

        // 4. Verification: 重要！例外が起きても Dispose が呼ばれたか
        /*
         * Verify (ちゃんとやった？)
         *「戻り値」がない処理（Dispose やログ出力など）が、
         * 内部で正しく実行されたかを後から確認するコマンドです。
         */
        /*
         * これはMoq（モックフレームワーク）の真骨頂とも言える機能で、
         * 「目に見えない実行結果を、スパイに報告させる」ためのコードです。
         * 一言で言うと、「scopeMock（身代わり）の Dispose メソッドが、
         * プログラムの実行中に『ちょうど1回だけ』呼び出されたかをチェックせよ」という意味になります。
         * 
         * (1) x => x.Dispose() （何をチェックするか）
         *     これは「検証したいアクション」を指定しています。
         *     「scopeMock に対して Dispose() という操作が行われたかどうかを調べてください」
         *     とMoqに命令しています。
         *     
         * (2) Times.Once （何回行われたか）
         *     ここが非常に重要です。期待する呼び出し回数を指定します。
         *     Times.Once: 「1回だけ」呼ばれたなら合格。
         *     Times.Never: 「1回も」呼ばれなかったら合格。
         *     Times.Exactly(3): 「ちょうど3回」呼ばれたら合格。
         *     今回のテストでは、using ブロックを抜ける際に 「必ず1回だけ確実に」 破棄されてほしいので、
         *     Once を指定しています。
         */
        /*
         * なぜこの検証が必要なのか？
         *     今回のテスト対象である ApiExecutor.RunAsync の中身を思い出してみましょう。
         *     // ApiExecutor.cs のイメージ
         *     public async IAsyncEnumerable<TResponse> RunAsync(...)
         *     {
         *         using var scope = _serviceProvider.CreateScope(); // ここで scope が作られる
         *         try {
         *             // ... ここで例外が発生して処理が中断される ...
         *         }
         *         catch (Exception ex) {
         *             throw; // 例外を外に投げる
         *         }
         *         // using の効果で、ここで自動的に scope.Dispose() が呼ばれるはず！
         *     }
         * 
         *     このテストの最大の目的は、「例外が起きて処理が途中で吹き飛んでも、
         *     .NETの using の仕組み（または finally）によって、リソースの片付け（Dispose）
         *     がサボられずに実行されたか」 を証明することです。
         *     もし Verify が失敗して「0回しか呼ばれていない」と報告されたら、それは
         *     リソースリーク（メモリやDB接続が開きっぱなしになる状態）が発生していることを意味します。
         */
        scopeMock.Verify(x => x.Dispose(), Times.Once, "例外発生時でも Scope は破棄されるべきです。");
    }

    // DIコンテナ構築・Executor実行（共通処理）
    private async Task<List<TResponse>> InvokeTestExecutor<TService, TRequest, TResponse>(
        Func<TRequest> createRequest)
        where TService : class, IApiService<TRequest, TResponse>
        where TRequest : RequestBase
        where TResponse : ResponseBase
    {
        // DIコンテナを構築
        var services = new ServiceCollection();

        // ApiExecutorを登録
        services.AddTransient<ApiExecutor>();

        // TServiceのインスタンス化
        services.AddTransient<TService>(sp =>
            (TService)Activator.CreateInstance(typeof(TService), _connectionString)!);

        // コンテナをビルド
        var serviceProvider = services.BuildServiceProvider();
        // executorをコンテナから取り出す
        var executor = serviceProvider.GetRequiredService<ApiExecutor>();

        // 実行
        List<TResponse> results = [];
        await foreach (var response in executor.RunAsync<TService, TRequest, TResponse>(createRequest()))
        {
            results.Add(response);
        }
        return results;
    }

    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_B1Service実行_01(decimal deptNo)
    {
        List<B1Response> results =
            await InvokeTestExecutor<B1Service, B1Request, B1Response>(() =>
                new B1Request
                {
                    DEPTNO = deptNo
                }
            );
        // 例外が発生しなければOK
    }

    [Theory]
    [InlineData(10)]
    public async Task RunAsync_正常系_B1Service_Test実行_01(decimal deptNo)
    {
        List<B1Response> results =
            await InvokeTestExecutor<B1Service_Test, B1Request, B1Response>(() =>
                new B1Request
                {
                    DEPTNO = deptNo
                }
            );
        // 例外が発生しなければOK
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
    [Fact]
    public async Task RunAsync_異常系_ApiExecutor実行キャンセル確認_01()
    {
        // 1. 準備 (Arrange)
        var services = new ServiceCollection();
        var mockService = new Mock<IB1Service>();

        /*
         * 「即座に作業を拒否するサービス」の身代わりです。
         * [EnumeratorCancellation] を付けることで、呼び出し元のキャンセル信号を
         * 正しく受け取れるようになっています。
         */
        async IAsyncEnumerable<B1Response> CancelledStream(
            B1Request req,
            [EnumeratorCancellation] CancellationToken ct) // [EnumeratorCancellation]を付加し警告を解消
        {
            /*
             * ct.ThrowIfCancellationRequested() は、キャンセル信号を受け取っている場合に
             * OperationCanceledException（またはその派生クラス）をスローする標準的な書き方です。
             */
            ct.ThrowIfCancellationRequested();
            yield break;
        }

        mockService
            .Setup(s => s.ExecuteAsync(It.IsAny<B1Request>(), It.IsAny<CancellationToken>()))
            .Returns((B1Request req, CancellationToken ct) => CancelledStream(req, ct));

        services.AddScoped(_ => mockService.Object);
        var provider = services.BuildServiceProvider();

        var executor = new ApiExecutor(provider);
        var request = new B1Request { DEPTNO = 10 };

        /*
         * 「キャンセルボタン」の役割です。
         * cts.Cancel() を呼ぶことで、実行中の処理に「止まってください」
         * という合図を送ります。
         */
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 「実行前にすでにキャンセルボタンが押された状態」を作り出しています。

        // 2. 実行 & 3. 検証 (Act & Assert)
        /*
         * 例外の検知: ApiExecutor が内部で例外を握りつぶさず、呼び出し元（テストコード）
         * まで正しくエラーを投げ返してきたかをチェックしています。
         * 型のリラックス: ThrowsAnyAsync<OperationCanceledException> とすることで、
         * .NET 内部で発生する TaskCanceledException も含めて、
         * キャンセル関連の例外であれば合格としています。
         */
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            /*
             * WithCancellation(cts.Token) を使うことで、先ほど作成したローカル関数
             * CancelledStream の引数 ct に、キャンセル済みのトークンが流し込まれます。
             */
            var stream = executor.RunAsync<IB1Service, B1Request, B1Response>(request, cts.Token);

            // WithCancellation(cts.Token) によって引数 ct にトークンが流し込まれる
            await foreach (var item in stream.WithCancellation(cts.Token))
            {
            }
        });
    }

    // OracleExceptionをテストで生成するためのヘルパーメソッド
    // (OracleExceptionはコンストラクタがinternalなのでリフレクション等で工夫が必要な場合があります)
    private Exception CreateOracleException(int number, string message)
    {
        // 厳密なOracleExceptionの生成が困難な場合は、
        // モック等で代替するか、型判定が通ることを優先します。
        // ここでは概念的な実装イメージです。
        return new InvalidOperationException($"Mock Oracle Error {number}: {message}");
    }

    [Fact]
    public async Task RunAsync_異常系_OracleExceptionハンドリング確認_01()
    {
        /*
         * このテストではOracleExceptionではなくExceptionがスローされる
         */
        // Arrange
        var services = new ServiceCollection();
        var mockService = new Mock<IB1Service>();

        // OracleException は public コンストラクタがないため、
        // 実際には発生させにくいですが、Moqでは例外をスローするように設定できます。
        // ※OracleExceptionのシミュレーションにはリフレクションが必要な場合がありますが、
        //   ここでは「OracleExceptionを投げる」スタブ動作を定義します。

        async IAsyncEnumerable<B1Response> OracleErrorStream()
        {
            yield return new B1Response { EMPNO = 1 }; // 1件目は成功

            // OracleExceptionを模した例外を投げる（テスト用に適当なエラーコードを想定）
            // ※実際にはOracleExceptionは継承できないため、Mockで作成するか、
            //   何らかの方法でインスタンス化する必要があります。
            //   ここでは簡単のため、動作確認としてExceptionを投げ、switch文を通ることを検証します。
            throw CreateOracleException(12154, "TNS:could not resolve the connect identifier");
        }

        mockService.Setup(s => s.ExecuteAsync(It.IsAny<B1Request>(), It.IsAny<CancellationToken>()))
                   .Returns(OracleErrorStream());

        services.AddScoped(_ => mockService.Object);
        var provider = services.BuildServiceProvider();
        var executor = new ApiExecutor(provider);

        // Act & Assert
        // コンソール出力（Console.WriteLine）の内容を検証したい場合は、TextWriterを差し替えますが、
        // ここでは「例外が再スローされること」を確認します。
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var item in executor.RunAsync<IB1Service, B1Request, B1Response>(new B1Request { DEPTNO = 10 }))
            {
                // 1件目は処理されるが、2件目の MoveNextAsync で例外が飛ぶ
            }
        });
    }

    //[Fact]
    [Fact(Skip = "DB停止状態の確認用")]
    public async Task RunAsync_異常系_OracleExceptionハンドリング確認_02_Oracleサービス停止時()
    {
        /*
         * OracleException捕捉確認
         * 【注】OracleDBサービス停止により例外を捕捉するため
         * サービスが正常に稼働している場合はこのテストは失敗する
         */
        // 出力メッセージを確認する場合は以下：
        //using var sw = new StringWriter();
        //Console.SetOut(sw); // 出力先を横取り

        // 1. Arrange: 本物のサービスを登録する
        var services = new ServiceCollection();

        // 実際の接続文字列を使用（テスト環境用の設定から取得）
        // データベースが停止している、あるいは接続できない状態を想定
        string connStr = _connectionString;

        // 本物のB1Serviceを登録
        services.AddTransient<IB1Service, B1Service>(sp => new B1Service(connStr));

        var provider = services.BuildServiceProvider();
        var executor = new ApiExecutor(provider);
        var request = new B1Request { DEPTNO = 10 };

        // 2. Act & 3. Assert
        try
        {
            // 実際にDBへ接続しに行く
            await foreach (var item in executor.RunAsync<IB1Service, B1Request, B1Response>(request))
            {
                // もしデータが取れてしまったら、サービスが動いているということ
            }

            // ここに到達した＝例外が発生せずに終了した
            // 「サービスを停止してから実行せよ」という意図を込めてテストを失敗させる
            Assert.Fail("Oracleサービスに正常に接続できてしまいました。サービスを停止してからテストを実行してください。");
        }
        catch (OracleException ox)
        {
            // 狙い通り OracleException が発生した
            // ApiExecutor内の switch 文で OracleException として処理されたことを間接的に証明
            Assert.True(ox.Number > 0, $"Oracleエラーが発生しました。Code: {ox.Number}");
            Console.WriteLine($"[期待通りの動作] OracleExceptionを捕捉しました: {ox.Message}");
        }
        catch (Exception ex)
        {
            // OracleException以外のエラー（ネットワーク不達以外のシステムエラーなど）
            Assert.Fail($"OracleExceptionを期待していましたが、別の例外が発生しました: {ex.GetType().Name} - {ex.Message}");
        }

        // 出力メッセージの確認
        // "[System Error]"の文字列が出力されていること
        //var output = sw.ToString();
        //Assert.Contains("[Database Error]", output);
    }

    [Fact]
    public async Task RunAsync_異常系_Exceptionハンドリング確認_01()
    {
        string expectMessage = "予期せぬシステムエラー";

        // Arrange
        var services = new ServiceCollection();
        var mockService = new Mock<IB1Service>();

        // 出力メッセージを確認する場合は以下：
        //using var sw = new StringWriter();
        //Console.SetOut(sw); // 出力先を横取り

        async IAsyncEnumerable<B1Response> SystemErrorStream()
        {
            yield return new B1Response { EMPNO = 1 }; // 1件目：成功
            throw new Exception(expectMessage); // 2件目：例外発生 ("予期せぬシステムエラー")
        }

        mockService.Setup(s => s.ExecuteAsync(It.IsAny<B1Request>(), It.IsAny<CancellationToken>()))
                   .Returns(SystemErrorStream());

            services.AddScoped(_ => mockService.Object);
        var provider = services.BuildServiceProvider();
        var executor = new ApiExecutor(provider);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(async () =>
        {
            await foreach (var item in executor.RunAsync<IB1Service, B1Request, B1Response>(new B1Request { DEPTNO = 10 }))
            {
            }
        });

        Assert.Equal(ex.Message, expectMessage);

        // 出力メッセージの確認
        // "[System Error]"の文字列が出力されていること
        //var output = sw.ToString();
        //Assert.Contains("[System Error]", output);
    }
}