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
        // Arrange
        var response = new B1Response { EMPNO = _empnoValue };
        var prop = typeof(B1Response).GetProperty(propertyName);

        // Act
        prop!.SetValue(response, null);

        // Assert
        Assert.Null(prop.GetValue(response));
    }

    /*
     * 以下はB1ResponseComparerで確認すべき？
     */
    [Fact]
    public void Equals_正常系_オブジェクト比較_一致確認_01()
    {
        /*
         * オブジェクトの一致テスト
         */
        var obj1 = CreateBase();
        var obj2 = CreateBase();
        var comparer = TEST_B1ResponseComparer.Default;

        // Act & Assert
        Assert.True(comparer.Equals(obj1, obj2));
    }

    [Theory]
    // 各ケース：(変更するプロパティ名, 変更後の値)
    [InlineData(nameof(B1Response.EMPNO), 9999)]
    [InlineData(nameof(B1Response.ENAME), "DIFFERENT")]
    [InlineData(nameof(B1Response.JOB), "CLERK")]
    [InlineData(nameof(B1Response.MGR), 1111)]
    [InlineData(nameof(B1Response.HIREDATE), "2020/01/01")]
    [InlineData(nameof(B1Response.SAL), 5000)]
    [InlineData(nameof(B1Response.COMM), 100)]
    [InlineData(nameof(B1Response.DEPTNO), 10)]
    [InlineData(nameof(B1Response.MGR), null)] // NULLへの変更チェック
    public void Equals_正常系_オブジェクト比較_不一致確認_01(string propertyName, object? newValue)
    {
        /*
         * オブジェクトの不一致テスト
         * １か所でも値が異なる場合は不一致とみなす
         */
        // Arrange
        var baseObj = CreateBase();
        var modifiedObj = CreateBase();

        // リフレクションを使って、指定されたプロパティ名だけ値を書き換える
        var prop = typeof(B1Response).GetProperty(propertyName);

        // decimal? などの型変換に対応するため Convert.ChangeType を利用
        object? convertedValue = (newValue == null)
            ? null
            : Convert.ChangeType(newValue, Nullable.GetUnderlyingType(prop!.PropertyType) ?? prop.PropertyType);

        prop!.SetValue(modifiedObj, convertedValue);

        var comparer = TEST_B1ResponseComparer.Default;

        // Act
        bool result = comparer.Equals(baseObj, modifiedObj);

        // Assert
        Assert.False(result, $"{propertyName} が変更された場合に False を返す必要があります。");
    }
}
