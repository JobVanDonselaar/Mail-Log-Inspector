using MailLogInspector.App;
using MimeKit;
using Xunit;

namespace MailLogInspector.Storage.Tests;

/// <summary>
/// Bewaakt de headers die bepalen of een bouncemelding in de inbox of in de spamfolder landt.
/// Zonder deze headers vult MimeKit de Windows-machinenaam in als Message-Id-domein, wat
/// ontvangende filters als een niet-bestaand domein beoordelen.
/// </summary>
public sealed class BounceNotificationMimeHeaderTests
{
    [Theory]
    [InlineData("mailloginspector@gmail.com", "gmail.com")]
    [InlineData("mailservice@uwtandarts.online", "uwtandarts.online")]
    [InlineData("Naam <info@sub.example.co.uk>", "sub.example.co.uk")]
    [InlineData("  meldingen@example.com  ", "example.com")]
    public void ResolveSendingDomain_TakesTheDomainFromTheSenderAddress(string address, string expected)
    {
        Assert.Equal(expected, BounceNotificationMimeBuilder.ResolveSendingDomain(address));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("geen-apenstaartje")]
    [InlineData("machinenaam@verhd2505")]
    [InlineData("leeg@")]
    public void ResolveSendingDomain_FallsBackWhenNoRealDomainIsPresent(string? address)
    {
        Assert.Equal("localhost.localdomain", BounceNotificationMimeBuilder.ResolveSendingDomain(address));
    }

    [Fact]
    public void Build_UsesTheSenderDomainForTheMessageIdInsteadOfTheMachineName()
    {
        MimeMessage mime = Build("mailloginspector@gmail.com");

        Assert.NotNull(mime.MessageId);
        Assert.EndsWith("@gmail.com", mime.MessageId, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.MachineName, mime.MessageId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_MarksTheReportAsAutomaticallyGenerated()
    {
        MimeMessage mime = Build("meldingen@example.com");

        Assert.Equal("auto-generated", mime.Headers[HeaderId.AutoSubmitted]);
    }

    [Fact]
    public void Build_OffersAnUnsubscribeRouteToTheSenderAddress()
    {
        MimeMessage mime = Build("meldingen@example.com");

        Assert.Equal("<mailto:meldingen@example.com?subject=Afmelden>", mime.Headers[HeaderId.ListUnsubscribe]);
    }

    /// <summary>
    /// De header laat afmelden via een antwoord lopen. Een standaardtekst die zegt dat antwoorden
    /// niet gelezen worden spreekt dat tegen, dus die twee moeten dezelfde route noemen.
    /// </summary>
    [Fact]
    public void DefaultFooterText_MatchesTheUnsubscribeRouteInTheHeader()
    {
        string? unsubscribe = Build("meldingen@example.com").Headers[HeaderId.ListUnsubscribe];
        Assert.Contains("subject=Afmelden", unsubscribe ?? string.Empty, StringComparison.Ordinal);

        Assert.Contains("Afmelden", BounceNotificationContentOptions.DefaultFooterText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "worden niet gelezen",
            BounceNotificationContentOptions.DefaultFooterText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_UnsubscribeRouteTrimsSurroundingWhitespaceFromTheAddress()
    {
        MimeMessage mime = Build("  meldingen@example.com  ");

        Assert.Equal("<mailto:meldingen@example.com?subject=Afmelden>", mime.Headers[HeaderId.ListUnsubscribe]);
        Assert.Equal("meldingen@example.com", mime.From.Mailboxes.Single().Address);
    }

    [Fact]
    public void Build_AcceptsAnAddressThatAlreadyCarriesItsOwnDisplayName()
    {
        MimeMessage mime = Build("Praktijk Meldingen <meldingen@example.com>", displayName: null);

        Assert.Equal("meldingen@example.com", mime.From.Mailboxes.Single().Address);
        Assert.Equal("Praktijk Meldingen", mime.From.Mailboxes.Single().Name);
        Assert.Equal("<mailto:meldingen@example.com?subject=Afmelden>", mime.Headers[HeaderId.ListUnsubscribe]);
    }

    [Fact]
    public void Build_LetsTheConfiguredDisplayNameWinOverTheOneInTheAddress()
    {
        Assert.Equal("Nieuw", Build("Oud <meldingen@example.com>", displayName: "Nieuw").From.Mailboxes.Single().Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("geen-apenstaartje")]
    public void Build_RefusesAnUnusableSenderAddressWithAReadableExplanation(string? address)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Build(address!));

        Assert.Contains("geen geldig e-mailadres", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One-Click afmelden vraagt volgens RFC 8058 om een https-adres dat de afmelding verwerkt.
    /// Dat is er niet, dus de header hoort weg te blijven.
    /// </summary>
    [Fact]
    public void Build_DoesNotClaimOneClickUnsubscribeSupport()
    {
        MimeMessage mime = Build("meldingen@example.com");

        Assert.Null(mime.Headers["List-Unsubscribe-Post"]);
    }

    [Fact]
    public void Build_KeepsSubjectSenderAndRecipientIntact()
    {
        MimeMessage mime = Build("meldingen@example.com", displayName: "Praktijk Meldingen");

        Assert.Equal("Praktijk Meldingen", mime.From.Mailboxes.Single().Name);
        Assert.Equal("meldingen@example.com", mime.From.Mailboxes.Single().Address);
        Assert.Equal("ontvanger@example.net", mime.To.Mailboxes.Single().Address);
        Assert.Equal("Bounce-overzicht", mime.Subject);
    }

    [Fact]
    public void Build_WithoutDisplayName_FallsBackToTheApplicationName()
    {
        MimeMessage mime = Build("meldingen@example.com", displayName: "   ");

        Assert.Equal("Mail Log Inspector", mime.From.Mailboxes.Single().Name);
    }

    [Fact]
    public void Build_GivesEveryMessageItsOwnIdentifier()
    {
        Assert.NotEqual(
            Build("meldingen@example.com").MessageId,
            Build("meldingen@example.com").MessageId);
    }

    private static MimeMessage Build(string fromAddress, string? displayName = "Mail Log Inspector") =>
        BounceNotificationMimeBuilder.Build(
            new BounceNotificationMessage(
                "ontvanger@example.net",
                "Bounce-overzicht",
                "<p>Overzicht</p>",
                "Overzicht",
                AttachmentPath: null,
                AttachmentFileName: null),
            fromAddress,
            displayName);
}
