namespace ServiceApi.Requests.C1;

/*
 * API「C1」のリクエストオブジェクト
 */
public record C1Request : RequestBase
{
    public required decimal DEPTNO { get; init; }
}
