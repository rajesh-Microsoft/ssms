using SMMS.Api.Services.Utilities;
using Xunit;

namespace SMMS.Api.Tests;

public class TGSPDCLHtmlParserTests
{
    private readonly TGSPDCLHtmlParser _parser = new();

    [Fact]
    public void Parse_ExtractsServerRenderedBill()
    {
        const string html = """
            <html><body>
              <table>
                <tr><td>Consumer Name</td><td>MS NEW LAND CONSTRUCTIONS</td></tr>
                <tr><td>Unique Service Number</td><td>115362356</td></tr>
                <tr><td>Service Number</td><td>0505 07958</td></tr>
                <tr><td>Units</td><td>1027</td></tr>
                <tr><td>Bill Date / Due Date</td><td>03-Aug-26 / 17-Aug-26</td></tr>
                <tr><td>Current Month Bill</td><td>9578</td></tr>
                <tr><td>Arrears</td><td>0</td></tr>
                <tr><td>Total Amount to be Paid</td><td>9578.00</td></tr>
              </table>
            </body></html>
            """;

        var result = _parser.Parse(html);

        Assert.NotNull(result);
        Assert.Equal("MS NEW LAND CONSTRUCTIONS", result.ConsumerName);
        Assert.Equal("0505 07958", result.ServiceNumber);
        Assert.Equal(new DateTime(2026, 8, 1), result.BillingMonth);
        Assert.Equal(new DateTime(2026, 8, 3), result.BillDate);
        Assert.Equal(new DateTime(2026, 8, 17), result.DueDate);
        Assert.Equal(9578m, result.BillAmount);
        Assert.Equal(1027m, result.UnitsConsumed);
        Assert.Equal(0m, result.Arrears);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenBillIsNotAvailable()
    {
        const string html = "<html><body><p>No bill available.</p></body></html>";

        Assert.Null(_parser.Parse(html));
    }
}