namespace ServiceApi.Responses.A1;

/// <summary>
/// 
/// </summary>
public class A1Response : ResponseBase
{
    /// <summary></summary>
    public required decimal Id { get; init; }
    /// <summary></summary>
    public string DataName { get; init; } = string.Empty;
}