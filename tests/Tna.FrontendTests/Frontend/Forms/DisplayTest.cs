namespace Tna.Frontend.Forms;

public sealed class DisplayTest
{
    [Theory]
    [InlineData(2026, 10, 11, "10/11（日）")]
    [InlineData(2026, 10, 7, "10/7（水）")]
    [InlineData(2027, 1, 2, "1/2（土）")]
    public void DateHasTheDayOfTheWeek(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, Display.Date(new DateOnly(year, month, day)));
    }

    // サービスの日付は日本時間なので、UTC の 15:00 に変わる
    [Fact]
    public void TodayChangesAtMidnightInJapan()
    {
        Assert.Equal(new DateOnly(2026, 10, 10), Display.Today(new ManualTime(new DateTimeOffset(2026, 10, 10, 14, 59, 59, TimeSpan.Zero))));
        Assert.Equal(new DateOnly(2026, 10, 11), Display.Today(new ManualTime(new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero))));
    }
}
