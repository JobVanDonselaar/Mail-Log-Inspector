namespace MailLogInspector.Core;

public sealed record SmtpLogEntry(
    int RowNumber,
    DateTime? AcceptedAt,
    DateTime? DeliveredAt,
    string MailFrom,
    string MailFromDomain,
    string Recipient,
    string RecipientDomain,
    string Status,
    string ResponseCode,
    string ResponseMessage,
    string BounceClass,
    int? Tries,
    string SenderId,
    string TrackingId,
    string CampaignId,
    string ResolvedCustomerId = "",
    string ResolvedCustomerName = "",
    string CustomerResolutionSource = "",
    double CustomerConfidence = 0.0);
