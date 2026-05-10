namespace ServiceApi.Requests.B1;

public record B1Request : RequestBase
{
    public required decimal DEPTNO { get; init; }
}
