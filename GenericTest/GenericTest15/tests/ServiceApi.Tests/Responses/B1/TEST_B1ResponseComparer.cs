using ServiceApi.Responses.B1;

namespace ServiceApi.Tests.Responses.B1;

public class TEST_B1ResponseComparer : TEST_ResponseComparerBase<B1Response>
{
    // staticなインスタンスを用意しておく
    public static TEST_B1ResponseComparer Default { get; } = new();

    // コンストラクタを private にして外部からの new を制限
    private TEST_B1ResponseComparer() { }

    protected override bool EqualsCore(B1Response x, B1Response y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;

        return x.EMPNO == y.EMPNO &&
               x.ENAME == y.ENAME &&
               x.JOB == y.JOB &&
               x.MGR == y.MGR &&
               x.HIREDATE == y.HIREDATE &&
               x.SAL == y.SAL &&
               x.COMM == y.COMM &&
               x.DEPTNO == y.DEPTNO;
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