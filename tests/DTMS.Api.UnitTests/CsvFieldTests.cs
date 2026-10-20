using DTMS.SharedKernel.Text;
using FluentAssertions;

namespace DTMS.Api.UnitTests;

// Frontend review M27: the report CSVs carry OMS / free-text values, and
// Excel runs a cell starting with = + - @ as a formula.
public class CsvFieldTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tx", "'\tx")]
    public void FormulaLikeCell_IsDefused(string input, string expected)
        => CsvField.Escape(input).Should().Be(expected);

    [Theory]
    [InlineData("-5", "-5")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("OD-161026-0207-BUSRUN", "OD-161026-0207-BUSRUN")]
    [InlineData("หมายเหตุ ภาษาไทย", "หมายเหตุ ภาษาไทย")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void OrdinaryCell_KeepsItsValue(string? input, string expected)
        => CsvField.Escape(input).Should().Be(expected);
}
