using ServiceApi.Responses;

namespace ServiceApi.Tests.Responses;

public abstract class TEST_ResponseComparerBase<TResponse> : IEqualityComparer<TResponse>
    where TResponse : ResponseBase
{
    /*
     * レスポンスクラスインスタンスの比較を行う
     */
    public bool Equals(TResponse? obj1, TResponse? obj2)
    {
        // 片方または両方が null の場合の基本チェック
        if (ReferenceEquals(obj1, obj2)) return true;
        if (obj1 == null || obj2 == null) return false;

        // 具体的な比較は派生クラスに任せる
        return EqualsCore(obj1, obj2);
    }

    public int GetHashCode(TResponse obj)
    {
        if (obj == null) return 0;
        return GetHashCodeCore(obj);
    }

    // 派生クラスで実装する抽象メソッド
    protected abstract bool EqualsCore(TResponse obj1, TResponse obj2);
    protected abstract int GetHashCodeCore(TResponse obj);
}