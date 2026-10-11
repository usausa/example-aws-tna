namespace Tna.Frontend.Forms;

using System.Globalization;

// 画面に出す値の書式。日付は月/日に曜日を添えた形にし、端末のタイムゾーンによらずサービスの時刻 (日本時間) で表す
public static class Display
{
    // 日本に夏時間はないので、時差は固定でよい
    private static readonly TimeSpan ServiceOffset = TimeSpan.FromHours(9);

    private static readonly string[] DaysOfWeek = ["日", "月", "火", "水", "木", "金", "土"];

    public static string Date(DateOnly date) =>
        String.Create(CultureInfo.InvariantCulture, $"{date.Month}/{date.Day}（{DaysOfWeek[(int)date.DayOfWeek]}）");

    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(ServiceOffset).DateTime);
    }
}
