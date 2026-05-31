namespace ServiceApi.Resources.Messages;

/*
 * メッセージID定義
 */
internal static class MessageId
{
    /// <summary>Service started. ({0})</summary>
    public const string MSG001 = "MSG001";
    /// <summary>Service completed.</summary>
    public const string MSG002 = "MSG002";
    /// <summary>Service aborted by exception.</summary>
    public const string MSG003 = "MSG003";
    /// <summary>Cancellation request detected.</summary>
    public const string MSG005 = "MSG005";

    /// <summary>Error reading Json ({0}): {1}</summary>
    public const string MSG991 = "MSG991";

}
