using System.Globalization;
using SMMS.Api.Models;

namespace SMMS.Api.Services.Payments;

/// <summary>
/// Default gateway: builds a dynamic UPI deep link for the charge. No external API call —
/// settlement is confirmed by the resident uploading proof and an admin approving it.
/// </summary>
public class ManualUpiPaymentGateway(QrService qr) : IPaymentGateway
{
    public string Name => "ManualUPI";

    public PaymentInstruction CreateInstruction(SocietySettings settings, Collection charge, Member member)
    {
        if (string.IsNullOrWhiteSpace(settings.UpiId))
            throw new InvalidOperationException("Society UPI ID is not configured.");

        var invoiceNumber = PaymentNumbering.InvoiceNumber(charge);
        var billingLabel = PaymentNumbering.BillingLabel(charge.Month, charge.Year);
        var payeeName = string.IsNullOrWhiteSpace(settings.UpiPayeeName) ? settings.SocietyName : settings.UpiPayeeName!;
        var note = QrService.BuildNote(invoiceNumber, member.Flat, billingLabel, member.Name);
        var uri = qr.BuildUpiUri(settings.UpiId!, payeeName, charge.Amount, note);

        return new PaymentInstruction("upi", uri, invoiceNumber, note, charge.Amount);
    }
}

/// <summary>Shared invoice-number / billing-label / due-date derivation used by services and DTOs.</summary>
public static class PaymentNumbering
{
    public static string InvoiceNumber(Collection c) => c.InvoiceNumber ?? $"INV{c.Id:D6}";

    public static string BillingLabel(int month, int year)
    {
        var monthName = month is >= 1 and <= 12
            ? CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month)
            : month.ToString(CultureInfo.InvariantCulture);
        return $"{monthName}{year}";
    }

    public static DateTime DueDate(Collection c, int dueDay)
    {
        if (c.DueDate.HasValue) return c.DueDate.Value;
        var day = Math.Clamp(dueDay, 1, DateTime.DaysInMonth(c.Year, c.Month));
        return new DateTime(c.Year, c.Month, day);
    }
}
