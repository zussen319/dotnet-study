namespace ServiceApi.Requests.C1;

public record C1Request : RequestBase
{
    public required decimal DEPTNO { get; init; }
}
