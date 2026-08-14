using System.Globalization;
using HtmlAgilityPack;

namespace SMMS.Api.Services.Utilities;

public class TGSPDCLHtmlParser
{
    private static readonly string[] DateFormats = ["dd-MMM-yy", "dd-MMM-yyyy", "dd/MM/yyyy", "dd-MM-yyyy"];

    public UtilityBillResult? Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var fields = ReadTableFields(document);

        if (!TryDecimal(fields, "Total Amount to be Paid", out var amount) &&
            !TryDecimal(fields, "Current Month Bill", out amount))
            return null;

        var (billDate, dueDate) = ParseBillAndDueDates(Get(fields, "Bill Date / Due Date"));
        var billingMonth = new DateTime((billDate ?? DateTime.UtcNow).Year, (billDate ?? DateTime.UtcNow).Month, 1);

        TryDecimal(fields, "Units", out var units);
        TryDecimal(fields, "Arrears", out var arrears);

        return new UtilityBillResult(
            billingMonth,
            Get(fields, "Bill Number"),
            billDate,
            dueDate,
            amount,
            fields.ContainsKey("Units") ? units : null,
            arrears,
            Get(fields, "Consumer Name"),
            Get(fields, "Service Number"),
            amount > 0 ? "Outstanding" : "Paid",
            html);
    }

    private static Dictionary<string, string> ReadTableFields(HtmlDocument document)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = document.DocumentNode.SelectNodes("//tr");
        if (rows is null) return fields;
        foreach (var row in rows)
        {
            var cells = row.SelectNodes("./th|./td");
            if (cells is null || cells.Count < 2) continue;
            var key = Clean(cells[0].InnerText).TrimEnd(':');
            var value = Clean(cells[1].InnerText);
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value)) fields[key] = value;
        }
        return fields;
    }

    private static string Clean(string value) => HtmlEntity.DeEntitize(value).Replace('\u00a0', ' ').Trim();

    private static string? Get(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : null;

    private static bool TryDecimal(IReadOnlyDictionary<string, string> fields, string key, out decimal value)
    {
        value = 0;
        return fields.TryGetValue(key, out var text) &&
               decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                   CultureInfo.GetCultureInfo("en-IN"), out value);
    }

    private static (DateTime? BillDate, DateTime? DueDate) ParseBillAndDueDates(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return (ParseDate(parts.ElementAtOrDefault(0)), ParseDate(parts.ElementAtOrDefault(1)));
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
}