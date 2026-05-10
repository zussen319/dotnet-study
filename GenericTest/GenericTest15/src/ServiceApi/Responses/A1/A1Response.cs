namespace ServiceApi.Responses.A1;

/*
 * API「A1」のレスポンスオブジェクト
 */
public record A1Response : ResponseBase
{
    public required decimal Id { get; init; }
    public string DataName { get; init; } = string.Empty;
}