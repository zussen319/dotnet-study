using ServiceApi.Responses.B1;

namespace ServiceApi.Tests.Responses.B1;

public class TEST_B1ResponseComparer : TEST_ResponseComparerBase<B1Response>
{
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

public class ComparerTest
{
    /*
     * TEST_B1ResponseComparer がすべてのプロパティを正しく比較できているかを検証する
     */
    [Fact]
    public void Equals_全プロパティ一致時True返却()
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
    public void Equals_プロパティ不一致時False返却(decimal empno, string ename)
    {
        // Arrange
        var obj1 = new B1Response { EMPNO = 1, ENAME = "A" };
        var obj2 = new B1Response { EMPNO = empno, ENAME = ename };
        var comparer = TEST_B1ResponseComparer.Default;

        // Act & Assert
        Assert.False(comparer.Equals(obj1, obj2));
    }

    [Fact]
    public void GetHashCode_ハッシュコード一致確認()
    {
        // Arrange
        var obj1 = new B1Response { EMPNO = 100, ENAME = "KING" };
        var obj2 = new B1Response { EMPNO = 100, ENAME = "KING" };
        var comparer = TEST_B1ResponseComparer.Default;

        // Act & Assert
        Assert.Equal(comparer.GetHashCode(obj1), comparer.GetHashCode(obj2));
    }

#if false
    /*
     * リスト内存在チェック
     */
    [Fact]
    public void List_Contains_リスト内存在チェック_該当あり()
    {
        // Arrange
        var target = new B1Response { EMPNO = 10, ENAME = "ACCOUNTING" };
        var list = new List<B1Response>
        {
            new B1Response { EMPNO = 20, ENAME = "RESEARCH" },
            new B1Response { EMPNO = 10, ENAME = "ACCOUNTING" } // これを見つけたい
        };

        // Act
        // 自作したComparerを第2引数に渡す
        bool exists = list.Contains(target, TEST_B1ResponseComparer.Default);

        // Assert
        Assert.True(exists);
    }
#endif

    /*
     * リスト内存在チェック
     */
    [Theory]
    [InlineData(10, "ACCOUNTING", true)] // 一致あり（完全一致）
    [InlineData(20, "ACCOUNTING", false)] // 一致なし（部分一致：不一致とみなす）
    [InlineData(99, "TEST", false)] // 一致なし
    public void List_Contains_リスト内存在チェック(decimal empNo, string ename, bool expectResult)
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
