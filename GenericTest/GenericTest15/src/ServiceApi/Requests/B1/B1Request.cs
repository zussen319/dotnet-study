namespace ServiceApi.Requests.B1;

/*
 * API「B1」のリクエストオブジェクト
 */
public record B1Request : RequestBase
{
    public required decimal DEPTNO { get; init; }
}
