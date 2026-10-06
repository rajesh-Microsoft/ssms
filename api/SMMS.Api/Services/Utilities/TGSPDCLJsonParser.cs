using System.Globalization;
using System.Text.Json;

namespace SMMS.Api.Services.Utilities;

public class TGSPDCLJsonParser
{
    private static readonly string[] DateFormats = ["dd-MMM-yy", "dd-MMM-yyyy", "dd/MM/yyyy", "dd-MM-yyyy"];

    public UtilityBillResult? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            (!TryDecimal(root, "AMOUNTPAYABLE", out var amount) &&
             !TryDecimal(root, "BILLAMT", out amount)))
            return null;

        var billDate = ParseDate(GetString(root, "BILLDATE"));
        var dueDate = ParseDate(GetString(root, "DUEDATE"));
        var billingMonth = new DateTime((billDate ?? DateTime.UtcNow).Year, (billDate ?? DateTime.UtcNow).Month, 1);

        TryDecimal(root, "UNITS", out var units);
        TryDecimal(root, "ARRAMT", out var arrears);

        return new UtilityBillResult(
            billingMonth,
            GetString(root, "BILLNO"),
            billDate,
            dueDate,
            amount,
            root.TryGetProperty("UNITS", out _) ? units : null,
            arrears,
            GetString(root, "CUSTOMERNAME"),
            GetString(root, "SERVICENO"),
            amount > 0 ? "Outstanding" : "Paid",
            json);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : property.GetRawText().Trim();
    }

    private static bool TryDecimal(JsonElement root, string propertyName, out decimal value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var property) &&
               (property.ValueKind == JsonValueKind.Number
                   ? property.TryGetDecimal(out value)
                   : decimal.TryParse(property.GetString(), NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                       CultureInfo.GetCultureInfo("en-IN"), out value));
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
}