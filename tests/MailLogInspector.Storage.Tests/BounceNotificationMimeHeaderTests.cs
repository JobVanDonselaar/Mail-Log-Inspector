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
