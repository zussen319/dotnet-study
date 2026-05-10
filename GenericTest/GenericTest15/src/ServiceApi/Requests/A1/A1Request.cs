namespace ServiceApi.Requests.A1;

public record A1Request : RequestBase
{
    public required decimal A1Value { get; init; }
}
