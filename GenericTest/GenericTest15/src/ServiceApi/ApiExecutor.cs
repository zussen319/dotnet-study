using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using ServiceApi.Requests;
using ServiceApi.Resources.Messages;
using ServiceApi.Responses;
using ServiceApi.Services;
using System.Runtime.CompilerServices;

namespace ServiceApi;

//
// サービスの生成・実行・破棄のライフサイクルを管理します
//
public class ApiExecutor(IServiceProvider appServices) : IApiExecutor
{
    /*
     * "IServiceProvider appServices"について
     * 
     * appServices に渡されるのは、メインプログラムで appHostBuilder.Build() 
     * を実行した瞬間に完成した「DIコンテナ（依存関係の巨大な辞書）」の実体です。
     * 
     * [1] メインプログラムとの紐付け
     * メインプログラムの以下のコード
     *     // (1) サービスを登録する
     *     var appBuilder = Host.CreateApplicationBuilder(args);
     *     appBuilder.Services.AddTransient<IApiExecutor, ApiExecutor>(); // ★ここで登録
     *     // (2) コンテナ（辞書）を完成させる
     *     using IHost appHost = appBuilder.Build(); 
     *     // (3) インスタンスを取り出す
     *     var executor = appHost.Services.GetRequiredService<IApiExecutor>();
     * この (3) で GetRequiredService<IApiExecutor>() が呼ばれたとき、
     * .NETのシステムは次のように動きます。
     *   1.「IApiExecutor には ApiExecutor クラスを使えばいいんだな」と辞書を引く。
     *   2.「ApiExecutor の生成には IServiceProvider（引数名：appServices）が必要だな」と判断する。
     *   3.自分自身（appHost.Services）を、そのまま ApiExecutor のコンストラクタに放り込む。
     * つまり、appServices の中身は appHost.Services そのもの です。
     * 
     * [2] なぜ IServiceProvider が「何でも知っている」のか
     * appServices（IServiceProvider）は、いわば 「サービスの詰まったカタログ」 です。
     * メインプログラム側で appBuilder.Services.AddTransient などを使って登録した内容は
     * すべてこのカタログに載っています。
     * - IApiExecutor
     * - IB1Service
     * - IConfiguration（設定値）
     * - ILogger（ログ出力機能）
     * これらがすべて appServices という一つの窓口に集約されています。
     * 
     * [3] ApiExecutor がそれを受け取る理由
     * ApiExecutor がなぜこの巨大なカタログ（appServices）をわざわざ受け取っているのかというと、
     * 「後で、必要な時に、必要なサービス（B1Serviceなど）を、正しいスコープで取り出すため」です。
     * これを 「サービスロケーターパターン」的なDIの利用法 と呼びます。
     * 1.ApiExecutor は最初、自分がどのサービス（B1なのかC1なのか）を動かすか知りません。
     * 2.RunAsync<TService, ...> が呼ばれた瞬間に初めて、「あ、今回は TService が必要だ」とわかります。
     * 3.そこで、持っておいたカタログ（appServices）から、その場で TService を取り出して（解決して）実行します。
     */

    public async IAsyncEnumerable<TResponse> RunAsync<TService, TRequest, TResponse>(
        IEnumerable<TRequest> requests,
        [EnumeratorCancellation] CancellationToken ct = default) // 非同期ストリームのキャンセルを有効化
        where TService : IApiService<TRequest, TResponse>
        where TRequest : RequestBase
        where TResponse : ResponseBase
    {
        // IAsyncEnumerable を扱うため、Scopeの寿命管理に注意が必要です
        /*
        * ## リソース管理（Scopeとusing）の安全性
        * 非同期ストリームにおいて最も難しいのは「いつDB接続を閉じるか」ですが、
        * ここでは以下のように解決しています。
        * 1. **"using var scope"**: "ApiExecutor" 内でスコープを作っています。
        * 2. **"await foreach" の連鎖**: 呼び出し元（メイン処理）がすべてのデータを読み終わる
        * （または途中で終了する）まで、"ApiExecutor" の "RunAsync"メソッドは
        * 「実行中」の状態を維持します。
        * 3. **自動破棄**: 呼び出し元で "await foreach" が終わると
        * 呼び出し階層を遡って "ApiExecutor" の "using scope"が抜け、
        * 最終的に "ServiceBase の "Dispose"（接続解除）が実行されます。
        */
        /*
         * 「スコープ」とはリソースの「有効期限」
         * スコープ（IServiceScope）は「使い終わったらゴミ箱に捨てる単位」を定義します。
         * メインプログラムの"appHost"は、プログラムの開始から終了まで生き続ける「全体スコープ」です。
         * それに対してApiExecutor内のスコープは、1回の"RunAsync"1回のバッチ処理実行）
         * の間だけ生きる「一時スコープ」です。
         * 
         * なぜ ApiExecutor でスコープを作るのか？
         * 最大の理由は、ServiceBase（Oracle接続）の Dispose を確実に行うためです。
         * [A] もしスコープを作らなかったら？
         *     ApiExecutor がメイン側の appHost.Services から直接 B1Service を取り出したとします。
         *     (1)B1Service が生成され、Oracleとのコネクションが開かれます。
         *     (2)処理が終了します。
         *     (3)しかし、B1Service はメインプログラム（appHost）が終了するまでメモリ上に残り続け、
         *        Oracleのセッションも開きっぱなしになります。
         *     (4)バッチが連続して動くと、DBの接続上限に達してパンクします。
         * 
         * [B] スコープを作った場合
         *     (1)using var scope = ... で一時的な区画を作ります。
         *     (2)その区画の中で B1Service を生成します。
         *     (3)処理が終わり、using を抜けて scope が破棄されるとき、DIコンテナが
         *       「このスコープで作った B1Service はもういらないな」と判断し、
         *        自動的に Dispose（接続解除）を呼んでくれます。
         * 
         * メイン側で意識しなくて良い理由
         * ・メインプログラムは「家全体の管理者」のようなものです。
         *   管理者は家が壊れる（プログラムが終了する）まで引退しません。
         * ・一方、ApiExecutor は「個別の仕事（タスク）」を担当します。
         *   仕事が終わるたびに道具（ServiceやDB接続）を片付ける必要があるため、
         *   仕事ごとに「スコープ（作業箱）」を用意しているのです。
         * 
         * スコープによるリソース管理
         * ◆メインプログラム側(B1Test.cs)：Root Scope (appHost):
         *   IApiExecutor（メイン側の処理中ずっと使う）
         * ◆ApiExecutor側：Child Scope (using scope):
         *   IB1Service（この処理が終わったら捨てたい）
         *   OracleConnection（サービスと一緒に閉じたい）
         * 
         * 「シングルトンではない、寿命が短いオブジェクトを外部から呼び出すクラス」
         * を書く際の、DIの鉄板パターンです。
         * 
         * 特に今回のように、ServiceBase が IDisposable（または IAsyncDisposable）
         * を継承して OracleConnection を保持している場合、この CreateScope() がないと
         * メモリリークやDB接続リークの直接的な原因になります。
         */
#if true
        await using var scope = appServices.CreateAsyncScope();
#else
        using IServiceScope scope = appServices.CreateScope();
#endif
        // スコープからServiceインスタンスを取得
        /* 
         * スコープ内にServiceインスタンスを作成する
         * 
         * 1. 作成したServiceは「スコープの所有物」になる
         *    scope.ServiceProvider.GetRequiredService<TService>() 
         *    を通じてインスタンス化されたオブジェクトは、そのスコープが管理するリストに登録されます。
         * 
         * 2. 自動的な破棄（Dispose）の連鎖
         *    using var scope = ... のブロックを抜ける際、scope.Dispose() が呼ばれます。
         *    このとき、DIコンテナは「このスコープの中で作ったインスタンスのうち
         *    IDisposable や IAsyncDisposable を実装しているもの」をすべて洗い出し
         *    それらの Dispose() を自動的に実行します。
         * 
         * 3. 依存関係も一緒に片付く
         *    もし B1Service が内部で別のリソース（例えばロガーやリポジトリ）をDIされていた場合、
         *    それらも同じスコープ内で作られていれば、親であるサービスと一緒にまとめて破棄されます。
         * 
         * 今回のコードにおいて、ServiceBase は IDisposable と IAsyncDisposable を実装しており、
         * 内部で OracleConnection を保持しています。
         * もしスコープを使わずに ルート（appHost.Services）から取得してしまうと、
         * プログラムが終わるまで B1Service が居座り続け、Oracleの接続（セッション）が解放されません。
         * スコープを使うことで、ApiExecutor の一連の処理が終わった瞬間に B1Service の Dispose が走り、
         * 結果として OracleConnection.Close() が確実に実行されます。
         */
        TService service = scope.ServiceProvider.GetRequiredService<TService>();
        // ExecuteAsync実行
        var enumerator = service.ExecuteAsync(requests, ct).GetAsyncEnumerator(ct);

        // 正常終了したかどうかを管理するフラグ
        bool isCompleted = false; // 未完了

        try
        {
            // 処理開始ログ出力
            Console.WriteLine(MessageResourceProvider.GetMessage(MessageId.MSG001, service.GetType().Name));

            while (true)
            {
                /*
                 * 1. 例外捕捉のタイミング：
                 * IAsyncEnumerable において、実際の重い処理（DB接続やSQL実行）が動き出すのは
                 * ExecuteAsync を呼び出した瞬間ではなく、最初の MoveNextAsync() を await した
                 * タイミングであることがほとんどです。
                 * 接続失敗 (OpenAsync): 最初の MoveNextAsync() の内部で実行されるため、
                 * 内側の catch ブロックで捕捉されます。
                 * フェッチ中の切断 (ReadAsync): 100行目であろうと1万行目であろうと、
                 * 次のデータを読みに行くのは MoveNextAsync() です。これも内側の catch がしっかり捕まえます。
                 * 
                 * 2. 捕捉の伝播：
                 * この実装では、 catch ブロックの中でログを出力した後、再度 throw; を行っています。
                 * ApiExecutor でエラーの「詳細（Oracleエラーコードなど）」を標準出力に残す。
                 * その直後に例外を再送出 (re-throw) することで、呼び出し元の Program.cs にある
                 * try-catch がそれを検知し、アプリケーション全体としての共通終了処理
                 * （エラー終了のステータスコード設定など）を行えるようになっています。
                 * 
                 * 3. クリーンアップ：
                 * ここが非同期ストリームで最も重要なポイントですが、以下の2段構えで保護されています。
                 * DisposeAsync(): 途中で例外が発生して while ループを抜けたとしても、
                 * finally ブロックが必ず実行されます。そこで enumerator.DisposeAsync() が呼ばれることで
                 * ServiceBase 側の Reader も連動して閉じられます。
                 * using var scope: ApiExecutor 自体が例外で終了する際、このスコープが破棄されます。
                 * これにより、DIコンテナが管理している Service インスタンスの Dispose() も走り、
                 * Oracleコネクションが確実に解放されます。
                 */

                TResponse response;
                try
                {
                    // NomveNextAsync (次の行の取得) の失敗を catch する
                    // ctはGetAsyncEnumeratorで渡されているためMoveNextAsyncでは不要
                    if (!await enumerator.MoveNextAsync()) { break; }
                    response = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    // キャンセルは明示的に捕捉
                    string message = MessageResourceProvider.GetMessage(MessageId.MSG005);
                    Console.WriteLine(message);
                    throw; // メイン側へ通知
                }
                catch (Exception ex)
                {
                    // エラーメッセージ生成
                    string message = ex switch
                    {
                        // OracleException
                        OracleException ox => $"[Database Error] Code: {ox.Number}, Message: {ox.Message}",
                        // その他の例外
                        _ => $"[System Error] {ex.Message}"
                    };
                    Console.WriteLine(message);
                    throw;
                }

                // yield return は try-catch の外で行う
                /*
                 * C# では try-catch ブロックの内部で yield return を直接記述することができません
                 * （try-finally であれば可能ですが、 catch があるとコンパイルエラーになります）。
                 * これは、例外が発生した際に反復子の状態を安全に復元するのが難しいためです。
                 */
                yield return response;
            }

            isCompleted = true; // 処理完了
        }
        finally
        {
            // enumerator破棄
            await enumerator.DisposeAsync();

            // 処理終了ログ出力
            string message = (isCompleted
                ? MessageResourceProvider.GetMessage(MessageId.MSG002)   // 正常終了時
                : MessageResourceProvider.GetMessage(MessageId.MSG003)); // 異常終了時
            Console.WriteLine(message);
        }
    }
}
