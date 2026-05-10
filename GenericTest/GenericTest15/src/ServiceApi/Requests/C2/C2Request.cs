namespace ServiceApi.Requests.C2;

public record C2Request : RequestBase
{
    public required decimal DEPTNO { get; init; }
}
