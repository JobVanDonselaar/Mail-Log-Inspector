using System;

namespace MailLogInspector.Core;

/// <summary>
/// Eén ruwe logregel uit een gearchiveerd rapport: één afleverpoging voor één mail.
/// Deze regels staan bewust niet in de database om die klein en snel te houden;
/// ze worden op aanvraag rechtstreeks uit het archief gelezen.
/// </summary>
public sealed record MailLogInspectorMailHistoryAttempt(
    DateTime? AcceptedAt,
    DateTime? DeliveredAt,
    string Sender,
    string Recipient,
    string Status,
    string ResponseCode,
    string ResponseMessage,
    string BounceClass,
    int? Tries,
    string TrackingId,
    string SourceFileName)
{
    public DateTime SortMoment => DeliveredAt ?? AcceptedAt ?? DateTime.MinValue;

    /// <summary>
    /// Het moment waarop deze poging afgerond werd. Bij een uitgestelde poging is er geen
    /// afleverdatum, dan valt de logregel terug op het acceptatiemoment van de mail zelf.
    /// </summary>
    public string MomentDisplay
    {
        get
        {
            DateTime? moment = DeliveredAt ?? AcceptedAt;
            return moment.HasValue ? moment.Value.ToString("dd-MM-yyyy HH:mm") : "-";
        }
    }

    public string StatusDisplay => MailLogInspectorAttemptMeaning.DescribeRawStatus(Status);

    public string ResponseCodeDisplay => string.IsNullOrWhiteSpace(ResponseCode) ? "-" : ResponseCode.Trim();

    public string ResponseCodeMeaning => MailLogInspectorAttemptMeaning.DescribeResponseCode(ResponseCode);

    public string TriesDisplay => Tries?.ToString() ?? "-";

    public string ToolTipText
    {
        get
        {
            string accepted = AcceptedAt.HasValue ? AcceptedAt.Value.ToString("dd-MM-yyyy HH:mm") : "-";
            string delivered = DeliveredAt.HasValue ? DeliveredAt.Value.ToString("dd-MM-yyyy HH:mm") : "-";
            string bounce = string.IsNullOrWhiteSpace(BounceClass) ? "-" : BounceClass.Trim();
            return $"Geaccepteerd: {accepted}\nAfgerond: {delivered}\nOntvanger: {Recipient}\nBounceklasse: {bounce}\nBron: {SourceFileName}";
        }
    }
}
