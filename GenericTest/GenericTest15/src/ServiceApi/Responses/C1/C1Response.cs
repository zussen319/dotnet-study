namespace ServiceApi.Responses.C1;

#if true
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
}
#else
public class C1Response : ResponseBase
{
    public required decimal DEPTNO { get; init; }
    public string DNAME { get; init; } = string.Empty;

    public List<Emp> Employees { get; init; } = [];

    public class Emp
    {
        public required decimal EMPNO { get; init; }
        public string ENAME { get; init; } = string.Empty;
    }
}
#endif