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