namespace ServiceApi.Responses.B1;

/// <summary>
/// 
/// </summary>
public class B1Response : ResponseBase
{
    /// <summary></summary>
    public required decimal EMPNO { get; init; }
    /// <summary></summary>
    public string ENAME { get; init; } = string.Empty;
    /// <summary></summary>
    public string JOB { get; init; } = string.Empty;
    /// <summary></summary>
    public decimal? MGR { get; init; }
    /// <summary></summary>
    public string HIREDATE { get; init; } = string.Empty;
    /// <summary></summary>
    public decimal? SAL { get; init; }
    /// <summary></summary>
    public decimal? COMM { get; init; }
    /// <summary></summary>
    public decimal? DEPTNO { get; init; }
}