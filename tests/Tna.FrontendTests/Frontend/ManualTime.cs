namespace Tna.Frontend;

// テストが決める時計
internal sealed class ManualTime : TimeProvider
{
    public ManualTime(DateTimeOffset now)
    {
        Now = now;
    }

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
