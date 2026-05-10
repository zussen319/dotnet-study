namespace ServiceApi.Responses.B1;

/*
 * API「B1」のレスポンスオブジェクト
 */
public record B1Response : ResponseBase
{
    public required decimal EMPNO { get; init; }
    public string ENAME { get; init; } = string.Empty;
    public string JOB { get; init; } = string.Empty;
    public decimal? MGR { get; init; }
    public string HIREDATE { get; init; } = string.Empty;
    public decimal? SAL { get; init; }
    public decimal? COMM { get; init; }
    public decimal? DEPTNO { get; init; }
    /*
     * recordはToString()利用可（オーバーライド可）
     */
}