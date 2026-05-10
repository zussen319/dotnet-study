namespace ServiceApi.Responses.A1;

public class A1Response : ResponseBase
{
    public required decimal Id { get; init; }
    public string DataName { get; init; } = string.Empty;
}