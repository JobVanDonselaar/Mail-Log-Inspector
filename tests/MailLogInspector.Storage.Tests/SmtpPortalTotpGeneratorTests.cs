using MailLogInspector.App;
using Xunit;

namespace MailLogInspector.Storage.Tests;

public sealed class SmtpPortalTotpGeneratorTests
{
    [Fact]
    public void Generate_UsesStandardSha1ThirtySecondTotp()
    {
        const string rfcSecretBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(59);

        string code = SmtpPortalTotpGenerator.Generate(rfcSecretBase32, timestamp);

        Assert.Equal("287082", code);
    }

    [Fact]
    public void Generate_AcceptsGroupedSecret()
    {
        string code = SmtpPortalTotpGenerator.Generate(
            "GEZD GNBV GY3T QOJQ GEZD GNBV GY3T QOJQ",
            DateTimeOffset.FromUnixTimeSeconds(59));

        Assert.Equal("287082", code);
    }

    [Fact]
    public void GenerateWindow_TriesCurrentCodeBeforeAdjacentSteps()
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(59);

        IReadOnlyList<string> window = SmtpPortalTotpGenerator.GenerateWindow(secret, timestamp);

        Assert.Equal(SmtpPortalTotpGenerator.Generate(secret, timestamp), window[0]);
        Assert.Equal(3, window.Count);
    }}
