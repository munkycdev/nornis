using QRCoder;

namespace Nornis.Web.Services;

/// <summary>
/// A QR code as inline SVG, for a link the GM holds up at the table. Black on white on
/// purpose, whatever the theme: a scanner wants contrast, not the palette.
/// </summary>
public static class QrSvg
{
    public static string For(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(
            4, "#000000", "#FFFFFF", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
    }
}
