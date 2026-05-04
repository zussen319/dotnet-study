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
    public void Equals_ShouldReturnTrue_WhenAllPropertiesMatch()
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
    public void Equals_ShouldReturnFalse_WhenAnyPropertyDiffers(decimal empno, string ename)
    {
        // Arrange
        var obj1 = new B1Response { EMPNO = 1, ENAME = "A" };
        var obj2 = new B1Response { EMPNO = empno, ENAME = ename };
        var comparer = TEST_B1ResponseComparer.Default;

        // Act & Assert
        Assert.False(comparer.Equals(obj1, obj2));
    }

    [Fact]
    public void GetHashCode_ShouldBeSame_ForIdenticalObjects()
    {
        // Arrange
        var obj1 = new B1Response { EMPNO = 100, ENAME = "KING" };
        var obj2 = new B1Response { EMPNO = 100, ENAME = "KING" };
        var comparer = TEST_B1ResponseComparer.Default;

        // Act & Assert
        Assert.Equal(comparer.GetHashCode(obj1), comparer.GetHashCode(obj2));
    }

    /*
     * リスト内存在チェックのテスト
     */
    [Fact]
    public void List_Contains_ShouldWorkWithCustomComparer()
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
}
#endregion
