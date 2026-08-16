using SMMS.Api.Services.Utilities;
using Xunit;

namespace SMMS.Api.Tests;

public class HmwssbHtmlParserTests
{
    private readonly HmwssbHtmlParser _parser = new();

    // Mirrors the live markup: adjacent "label col" / "content col" divs.
    private const string BillHtml = """
        <html><body>
          <table><tr><td>CAN No.</td><td>623734473</td></tr></table>
          <div class="content-container">
            <div class="row">
              <div class="label col">Name : </div>
              <div class="content col">K.VENKATA NARAYANA NLC AARADYA,AADYA RESIDENCY </div>
              <div class="label col">Total Arrears : </div>
              <div class="content col">5618</div>
            </div>
            <div class="row">
              <div class="label col">Address : </div>
              <div class="content col">SY.NO 343/16,BANDAMKOMMU ,AMEENPUR,502032</div>
            </div>
          </div>
        </body></html>
        """;

    [Fact]
    public void Parse_ReadsOutstandingAmountAndName()
    {
        var result = _parser.Parse(BillHtml);

        Assert.NotNull(result);
        Assert.Equal(5618m, result!.BillAmount);
        Assert.Equal("K.VENKATA NARAYANA NLC AARADYA,AADYA RESIDENCY", result.ConsumerName);
        Assert.Equal("Outstanding", result.Status);
        Assert.Equal(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), result.BillingMonth);
    }

    [Fact]
    public void Parse_LeavesFieldsThePortalDoesNotPublishEmpty()
    {
        var result = _parser.Parse(BillHtml);

        Assert.Null(result!.BillNumber);
        Assert.Null(result.BillDate);
        Assert.Null(result.DueDate);
        Assert.Null(result.UnitsConsumed);
        Assert.Equal(0m, result.Arrears);
    }

    // BillDesk answers 200 with this message, so it must be detected from the body.
    [Fact]
    public void Parse_ReturnsNullForUnknownCan()
    {
        const string html = """
            <html><body><div>The Can Number you have entered is either invalid or there are
            no bills available at this moment.</div></body></html>
            """;

        Assert.Null(_parser.Parse(html));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body><div class=\"label col\">Name : </div><div class=\"content col\">X</div></body></html>")]
    public void Parse_ReturnsNullWhenAmountIsMissing(string html)
    {
        Assert.Null(_parser.Parse(html));
    }

    [Fact]
    public void Parse_HandlesThousandsSeparator()
    {
        var html = BillHtml.Replace(">5618<", ">12,345.50<");

        Assert.Equal(12345.50m, _parser.Parse(html)!.BillAmount);
    }
}
