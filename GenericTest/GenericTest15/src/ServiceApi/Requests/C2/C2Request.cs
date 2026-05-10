namespace ServiceApi.Requests.C2;

/*
 * API「C2」のリクエストオブジェクト
 */
public record C2Request : RequestBase
{
    public required decimal DEPTNO { get; init; }
}
