namespace ServiceApi.Responses.C1;

/*
 * API「C1」のレスポンスオブジェクト
 */
public record C1Response : ResponseBase
{
    public required decimal DEPTNO { get; init; }
    public string DNAME { get; init; } = string.Empty;
    public List<Emp> Employees { get; init; } = [];

    public record Emp
    {
        public required decimal EMPNO { get; init; }
        public string ENAME { get; init; } = string.Empty;
    }
    /*
     * recordはToString()利用可（オーバーライド可）
     */
    /*
     * 【補足】recordの比較（Equals）についての注意
     * List<T> を含んでいる場合、record 同士の"=="比較は
     * 「リストの中身」までは見ず、「同じリストのインスタンスか」をチェックする
     * ユニットテストで「中身が同じか」を厳密にチェックする場合は、以下の対応を検討する
     * (1) FluentAssertions などのライブラリを使う:
     *     actual.Should().BeEquivalentTo(expected) を使うのが最も簡単
     * (2) テスト時のみ ImmutableArray に変換して比較する:
     *     // テストコード内での比較イメージ
     *     Assert.Equal(expected.Employees.ToImmutableArray(), actual.Employees.ToImmutableArray());
     */
}
