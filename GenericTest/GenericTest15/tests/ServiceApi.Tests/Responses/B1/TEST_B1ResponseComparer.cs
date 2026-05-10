#if false
using ServiceApi.Responses.B1;

namespace ServiceApi.Tests.Responses.B1;

public class TEST_B1ResponseComparer : TEST_ResponseComparerBase<B1Response>
{
    /*
     * B1Responseオブジェクト比較のためのクラス（テストコード用）
     */

    // staticなインスタンスを用意しておく
    public static TEST_B1ResponseComparer Default { get; } = new();

    // コンストラクタを private にして外部からの new を制限
    private TEST_B1ResponseComparer() { }

    protected override bool EqualsCore(B1Response obj1, B1Response obj2)
    {
        if (ReferenceEquals(obj1, obj2)) return true;
        if (obj1 == null || obj2 == null) return false;

        return obj1.EMPNO == obj2.EMPNO &&
               obj1.ENAME == obj2.ENAME &&
               obj1.JOB == obj2.JOB &&
               obj1.MGR == obj2.MGR &&
               obj1.HIREDATE == obj2.HIREDATE &&
               obj1.SAL == obj2.SAL &&
               obj1.COMM == obj2.COMM &&
               obj1.DEPTNO == obj2.DEPTNO;
    }

    protected override int GetHashCodeCore(B1Response obj)
    {
        // 比較に使用したすべてのプロパティをハッシュ計算に含める
        var hash = new HashCode();
        hash.Add(obj.EMPNO);
        hash.Add(obj.ENAME);
        hash.Add(obj.JOB);
        hash.Add(obj.MGR);
        hash.Add(obj.HIREDATE);
        hash.Add(obj.SAL);
        hash.Add(obj.COMM);
        hash.Add(obj.DEPTNO);
        return hash.ToHashCode();
    }
}

#region テストコード
public class TEST_B1ResponseComparer_Test
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

    /*
     * TEST_B1ResponseComparer がすべてのプロパティを正しく比較できているかを検証する
     */
    [Fact]
    public void Equals_正常系_全プロパティ一致時True返却_01()
    {
        // Arrange
        var obj1 = new B1Response { EMPNO = 1, ENAME = "A" };
        var obj2 = new B1Response { EMPNO = 1, ENAME = "A" };
        var comparer = TEST_B1ResponseComparer.Default;

        // Act & Assert
        Assert.True(comparer.Equals(obj1, obj2));
    }

    [Theory]
    [InlineData(2, "A")] // EMPNOが違う
    [InlineData(1, "B")] // ENAMEが違う
    public void Equals_正常系_プロパティ不一致時False返却_01(decimal empno, string ename)
    {
        // Arrange
        var obj1 = new B1Response { EMPNO = 1, ENAME = "A" };
        var obj2 = new B1Response { EMPNO = empno, ENAME = ename };
        var comparer = TEST_B1ResponseComparer.Default;

        // Act & Assert
        Assert.False(comparer.Equals(obj1, obj2));
    }

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

    [Fact]
    public void GetHashCode_正常系_ハッシュコード一致確認_01()
    {
        // Arrange
        var obj1 = new B1Response { EMPNO = 100, ENAME = "KING" };
        var obj2 = new B1Response { EMPNO = 100, ENAME = "KING" };
        var comparer = TEST_B1ResponseComparer.Default;

        // Act & Assert
        Assert.Equal(comparer.GetHashCode(obj1), comparer.GetHashCode(obj2));
    }

    /*
     * リスト内存在チェック
     */
    [Theory]
    [InlineData(10, "ACCOUNTING", true)] // 一致あり（完全一致）
    [InlineData(20, "ACCOUNTING", false)] // 一致なし（部分一致：不一致とみなす）
    [InlineData(99, "TEST", false)] // 一致なし
    public void Contains_正常系_リスト内存在チェック_01(decimal empNo, string ename, bool expectResult)
    {
        // Arrange
        var target = new B1Response { EMPNO = empNo, ENAME = ename };
        var list = new List<B1Response>
        {
            new B1Response { EMPNO = 20, ENAME = "RESEARCH" },
            new B1Response { EMPNO = 10, ENAME = "ACCOUNTING" }
        };

        // Act
        // 自作したComparerを第2引数に渡す
        bool exists = list.Contains(target, TEST_B1ResponseComparer.Default);

        // Assert
        Assert.True(exists == expectResult);
    }
}
#endregion
#endif