using ServiceApi.Responses.B1;

namespace ServiceApi.Tests.Responses.B1;

public class TEST_B1Response
{
    // 初期値
    private decimal _empnoValue = 7788;
    private string _enameValue = "SCOTT";
    private string _jobValue = "ANALYST";
    private decimal? _mgrValue = 7566;
    private string _hiredateValue = "1987/04/19";
    private decimal? _salValue = 3000;
    private decimal? _commValue = null;
    private decimal? _deptnoValue = 20;

    // ベースとなる正常なデータを作成する補助メソッド
    private B1Response CreateBase() => new()
    {
        EMPNO = _empnoValue,
        ENAME = _enameValue,
        JOB = _jobValue,
        MGR = _mgrValue,
        HIREDATE = _hiredateValue,
        SAL = _salValue,
        COMM = _commValue,
        DEPTNO = _deptnoValue
    };

    [Fact]
    public void Properties_正常系_プロパティ初期値_01()
    {
        /*
         * プロパティの代入テスト
         * 基本的な初期化と値の保持を確認する
         * required 修飾子や init アクセサが正しく機能しているかをチェックする
         */
        /*
         * record のプライマリコンストラクタを使う場合は
         * 単なる「値の受け渡しテスト」になるため重要度は下がります。
         */
        // オブジェクトの作成テスト
        // new B1Response()が正常に動作することを確認する
        var response = CreateBase();

        // 結果確認
        Assert.Equal(response.EMPNO, _empnoValue);
        Assert.Equal(response.ENAME, _enameValue);
        Assert.Equal(response.JOB, _jobValue);
        Assert.Equal(response.MGR, _mgrValue);
        Assert.Equal(response.HIREDATE, _hiredateValue);
        Assert.Equal(response.SAL, _salValue);
        Assert.Equal(response.COMM, _commValue);
        Assert.Equal(response.DEPTNO, _deptnoValue);
    }

    [Fact]
    public void Properties_正常系_プロパティ初期値_02_string()
    {
        /*
         * デフォルト値のテスト
         * インスタンス化した直後に、string 型が null ではなく string.Empty になっているかを確認する
         */
        /*
         * これは有用なので残すべきです。
         * record に書き換えた際、string.Empty で初期化し忘れて
         * null になっていないかを保証するためです。
         */
        // Arrange & Act
        // EMPNO は required なので最小限の初期化
        var response = new B1Response { EMPNO = _empnoValue };

        // Assert
        Assert.NotNull(response.ENAME);
        Assert.Equal(response.ENAME, string.Empty);

        Assert.NotNull(response.JOB);
        Assert.Equal(response.JOB, string.Empty);

        Assert.NotNull(response.HIREDATE);
        Assert.Equal(response.HIREDATE, string.Empty);
    }

    [Theory]
    [InlineData(nameof(B1Response.MGR))]
    [InlineData(nameof(B1Response.SAL))]
    [InlineData(nameof(B1Response.COMM))]
    [InlineData(nameof(B1Response.DEPTNO))]
    public void Properties_正常系_プロパティ初期値_03_null許容decimal(string propertyName)
    {
        /*
         * Null 許容・禁止の網羅テスト（Theory を活用）
         * decimal? などの Nullable 型が正しく null を保持でき
         * 逆に required な項目が値を保持しているかを検証する
         */
        /*
         * リフレクションを使ったテストですが、record で init アクセサを使用すると
         * インスタンス作成後に SetValue で値を書き換えることは（基本的には）想定しません。
         * 型定義を見て decimal? になっていることが確認できれば十分なため、削除しても問題ありません。
         */
        // Arrange
        var response = new B1Response { EMPNO = _empnoValue };
        var prop = typeof(B1Response).GetProperty(propertyName);

        // Act
        prop!.SetValue(response, null);

        // Assert
        Assert.Null(prop.GetValue(response));
    }

#if true
    /*
     * Responseクラスのrecord化に伴う追加テスト
     */
    [Fact]
    public void Record_値ベースの比較が正しく機能すること()
    {
        // Arrange
        var res1 = new B1Response { EMPNO = 7788, ENAME = "SCOTT" };
        var res2 = new B1Response { EMPNO = 7788, ENAME = "SCOTT" };
        var res3 = new B1Response { EMPNO = 9999, ENAME = "SCOTT" };

        // Assert
        Assert.Equal(res1, res2);    // recordの標準機能でTrueになる
        Assert.NotEqual(res1, res3); // 1箇所でも違えばFalse
        Assert.True(res1 == res2);   // 演算子も自動実装される
    }

    [Fact]
    public void Properties_初期値がEmptyであること()
    {
        var response = new B1Response { EMPNO = 1 };
        Assert.Equal(string.Empty, response.ENAME);
        Assert.Equal(string.Empty, response.JOB);
    }

    [Fact]
    public void With式でのコピーが可能であること()
    {
        var original = new B1Response { EMPNO = 7788, ENAME = "SCOTT" };
        var updated = original with { ENAME = "NEW NAME" };

        Assert.Equal(7788, updated.EMPNO);
        Assert.Equal("NEW NAME", updated.ENAME);
    }
#endif
}
