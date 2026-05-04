using ServiceApi.Responses;

namespace ServiceApi.Tests.Responses;

public abstract class TEST_ResponseComparerBase<TResponse> : IEqualityComparer<TResponse>
    where TResponse : ResponseBase
{
    /*
     * レスポンスクラスインスタンスの比較を行う
     */
    public bool Equals(TResponse? x, TResponse? y)
    {
        // 片方または両方が null の場合の基本チェック
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;

        // 具体的な比較は子クラスに任せる
        return EqualsCore(x, y);
    }

    public int GetHashCode(TResponse obj)
    {
        if (obj == null) return 0;
        return GetHashCodeCore(obj);
    }

    // 子クラスで実装する抽象メソッド
    protected abstract bool EqualsCore(TResponse x, TResponse y);
    protected abstract int GetHashCodeCore(TResponse obj);
}