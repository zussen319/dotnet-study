using ApiProject;
using log4net;
using log4net.Config;

/*
 * ログ出力に関するAPI呼び出し元への伝達事項
 * 
 * log4netの初期化と構成ファイルの配置
 * API内部でlog4netを使用するため、エントリーポイント（Program.cs）での初期化が必須であることを伝えます。
 * 
 * [1] 初期化コードの記述: XmlConfigurator.Configure(...) をプログラム開始時に一度実行してもらう。
 * ファイルの配置: log4net.config ファイルを、実行ファイル（.exe）と同じディレクトリに配置してもらう
 * （またはプロジェクトに追加して「新しい場合はコピーする」設定にしてもらう）。
 * ＞ log4netの初期化
 * ＞ プログラム開始時に一度 XmlConfigurator.Configure(new FileInfo("log4net.config")); を実行してください。
 * ＞ log4net.config を実行環境に含めるよう設定をお願いします。
 * 
 * ### [2] 相関ID（CorrelationId）の生成と引数渡し
 * ### API側のログとメイン側の動きを紐付けるための鍵となる情報を約束します。
 * ### 生成ルール: API呼び出しごとに一意な文字列（Guid.NewGuid().ToString() など）を生成してもらう。
 * ### 引数: ApiService.Execute(int flag, string correlationId) のように、引数として確実に渡してもらう。
 * ### ＞ 相関ID（CorrelationId）の発行と受け渡し
 * ### ＞ API（ApiService.Execute）を呼び出す際、呼び出しごとに一意なID（GUID等）を生成し、第2引数として渡してください。
 * ### ＞ これにより、API内部のログにこのIDが自動付与され、調査が容易になります。
 * 
 * [3] 共通ログフォルダの作成権限
 * API側もメイン側も logs/ フォルダに書き込むため、実行環境においてそのフォルダへの書込み権限が
 * 必要であることを共有しておきます。
 * 
 * [4] 例外ハンドリングの責任範囲
 * API側でエラーが発生した場合、API側のログには詳細が残りますが、メインプログラム側でもそれを検知して
 * 処理を続行するか停止するかを制御してもらう必要があります。
 * 「API側で throw するので、メイン側で catch して上位の処理（ユーザーへの通知やリトライなど）を行ってほしい」
 * と伝えます。
 * ＞ 例外処理（try-catch）
 * ＞ API内部で異常が発生した際は例外をスローします。
 * ＞ メイン側で適切にキャッチし、必要に応じてメイン側のログにも記録をお願いします。
 */

// log4netの初期化（プログラム開始時に一度だけ実行）
XmlConfigurator.Configure(new FileInfo("log4net.config"));
ILog mainLog = LogManager.GetLogger(typeof(Program));

mainLog.Info("Main> Application start.");

var apiService = new ApiService();

try
{
    for (int flag = 1; flag <= 2; flag++)
    {
		// メイン側では相関IDを意識せず、純粋にAPIを呼び出すだけ
		mainLog.Info($"Main> Invoking API. [flag: {flag}]");
		var result = apiService.Execute(flag);
		mainLog.Info($"Main> API completed. [flag: {flag}]");
	}
}
catch (Exception ex)
{
	mainLog.Error("Main> API failed with critical error.", ex);
}

mainLog.Info("Main> Application end.");
