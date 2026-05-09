namespace ServiceApi.Responses.C1;

/// <summary>
/// 
/// </summary>
public class C1Response : ResponseBase
{
    /// <summary></summary>
    public required decimal DEPTNO { get; init; }
    /// <summary></summary>
    public string DNAME { get; init; } = string.Empty;

    /// <summary></summary>
    public List<Emp> Employees { get; init; } = [];

    /// <summary></summary>
    public class Emp
    {
        /// <summary></summary>
        public required decimal EMPNO { get; init; }
        /// <summary></summary>
        public string ENAME { get; init; } = string.Empty;
    }
}