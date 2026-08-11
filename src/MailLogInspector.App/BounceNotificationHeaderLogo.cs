using System.IO;

namespace MailLogInspector.App;

internal static class BounceNotificationHeaderLogo
{
    public const string ContentId = "exquise-next-logo";
    public const string FileName = "ExquiseNextMailLogo.png";
    private const string ResourceName = "MailLogInspector.App.Assets.ExquiseNextMailLogo.png";

    private static readonly Lazy<byte[]> CachedBytes = new(LoadBytes);

    public static byte[] Bytes => CachedBytes.Value;

    public static string ContentSource => $"cid:{ContentId}";

    public static string DataUri =>
        $"data:image/png;base64,{Convert.ToBase64String(Bytes)}";

    private static byte[] LoadBytes()
    {
        using Stream stream = typeof(BounceNotificationHeaderLogo).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Het ingebedde e-maillogo '{ResourceName}' kon niet worden geladen.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
