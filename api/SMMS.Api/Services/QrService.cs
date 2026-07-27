using System.Globalization;
using System.Text;
using QRCoder;

namespace SMMS.Api.Services;

/// <summary>
/// Builds NPCI-compliant UPI deep-link URIs (upi://pay?...) and renders them to PNG QR codes.
/// PngByteQRCode is used (not the System.Drawing renderer) so it works on Linux/containers.
/// </summary>
public class QrService
{
    /// <summary>
    /// Builds a UPI collect URI. The amount is fixed by the server so a resident can never
    /// edit it in their UPI app. The note is the human/receipt-traceable transaction reference.
    /// </summary>
    public string BuildUpiUri(string upiId, string payeeName, decimal amount, string note)
    {
        var sb = new StringBuilder("upi://pay?");
        sb.Append("pa=").Append(Uri.EscapeDataString(upiId));
        sb.Append("&pn=").Append(Uri.EscapeDataString(payeeName));
        sb.Append("&am=").Append(Uri.EscapeDataString(amount.ToString("0.00", CultureInfo.InvariantCulture)));
        sb.Append("&cu=INR");
        sb.Append("&tn=").Append(Uri.EscapeDataString(note));
        return sb.ToString();
    }

    /// <summary>Renders arbitrary text (here, a UPI URI) to a PNG byte array.</summary>
    public byte[] PngFromText(string text, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    /// <summary>Standard transaction note, e.g. "SMMS|Invoice:INV000245|Flat:A101|Month:Jul2026|Member:Rajesh".</summary>
    public static string BuildNote(string invoiceNumber, string flat, string billingLabel, string memberName)
        => $"SMMS|Invoice:{invoiceNumber}|Flat:{flat}|Month:{billingLabel}|Member:{memberName}";
}
