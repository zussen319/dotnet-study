using ServiceApi.Requests.B1;
using ServiceApi.Responses.B1;
using ServiceApi.Services.B1;

namespace ServiceApi.Tests.Services.B1;

public class TEST_B1Service_Test
{
    //[Fact] // このメソッドがテストであることを示す属性：「引数なし」の単一テスト用
    // [Theory] は「データ駆動（引数あり）」テスト用
    //[Theory]
    //[InlineData(920,3)] // ここで引数として渡したい値を指定
    //[InlineData(910,1)] // 複数の値を試したい場合は、行を増やすだけ
    //[InlineData(0,0)]   // 存在しない部門番号のテストなど
    //[Fact(DisplayName = "B1Service_Test：JSONデータ読み込み")]
    [Fact]
    public async Task ExecuteAsync_JSONデータ読み込み()
    {
        /*
         * 指定したJsonファイルを読み込み、B1Responseオブジェクトを生成できること
         */
        // Jsonファイルを読み込み、List<B1Response>オブジェクトを生成する
        string fileName = "B1Service_Test.json";
        var expectedList = TestDataLoader.LoadJsonData<B1Response>(fileName);

        // 1. 準備 (Arrange)
        // サービスをインスタンス化
        var service = new B1Service_Test("dummy_connection");
        var request = new B1Request { DEPTNO = 999 };

        // 2. 実行 (Act)
        var results = new List<B1Response>();
        await foreach (var item in service.ExecuteAsync(request))
        {
            // IAsyncEnumerableをListに変換して中身を検証しやすくする
            results.Add(item);
        }

        // 3. 検証 (Assert)
        // B1Service_Test.ExecuteAsyncがJsonファイルを正常に読み込み返却すること
        // ・例外が発生しないこと
        // ・テストコード内で読み込んだ結果と件数が一致すること
        Assert.Equal(expectedList.Count, results.Count);
    }
}