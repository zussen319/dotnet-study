namespace ServiceApi.Requests.A1;

/*
 * API「A1」のリクエストオブジェクト
 */
public record A1Request : RequestBase
{
    public required decimal A1Value { get; init; }
}
