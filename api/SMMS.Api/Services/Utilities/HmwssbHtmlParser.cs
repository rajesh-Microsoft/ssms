using System.Globalization;
using System.Net;
using HtmlAgilityPack;

namespace SMMS.Api.Services.Utilities;

/// <summary>
/// Reads the HMWSSB bill page served by BillDesk. The page publishes a single outstanding figure
/// ("Total Arrears") with no bill number, bill date, due date or consumption, so most of
/// <see cref="UtilityBillResult"/> is necessarily empty for this provider.
/// </summary>
public class HmwssbHtmlParser
{
    public UtilityBillResult? Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        // BillDesk answers 200 with this text for an unknown CAN, so status code alone proves nothing.
        if (html.Contains("either invalid or there are no bills", StringComparison.OrdinalIgnoreCase)) return null;

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var fields = ReadLabelledFields(document);

        if (!fields.TryGetValue("Total Arrears", out var raw) ||
            !decimal.TryParse(raw.Replace(",", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            return null;

        var now = DateTime.UtcNow;
        return new UtilityBillResult(
            new DateTime(now.Year, now.Month, 1),
            null,
            null,
            null,
            amount,
            null,
            0m,   // the page cannot separate arrears from the current charge
            fields.TryGetValue("Name", out var name) ? name : null,
            null,
            amount > 0 ? "Outstanding" : "Paid",
            html);
    }

    /// <summary>The page renders each pair as adjacent divs: label col then content col.</summary>
    private static Dictionary<string, string> ReadLabelledFields(HtmlDocument document)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nodes = document.DocumentNode.SelectNodes("//div[contains(@class,'col')]");
        if (nodes is null) return fields;

        for (var i = 0; i < nodes.Count - 1; i++)
        {
            if (!nodes[i].GetClasses().Contains("label")) continue;
            var label = Clean(nodes[i].InnerText).TrimEnd(':', ' ');
            if (label.Length == 0 || fields.ContainsKey(label)) continue;
            fields[label] = Clean(nodes[i + 1].InnerText);
        }
        return fields;
    }

    private static string Clean(string value) =>
        WebUtility.HtmlDecode(value ?? string.Empty).Replace('\u00a0', ' ').Trim();
}
