namespace Tna.Frontend.Pages;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

public sealed class HomeTest : BunitContext
{
    // 日本時間で 10/11 になった直後
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero));

    public HomeTest()
    {
        this.AddMudBlazor();
        Services.AddSingleton<TimeProvider>(time);
    }

    [Fact]
    public void HomeShowsTodayInJapan()
    {
        var page = Render<Home>();

        Assert.Equal("ホーム", page.Find("h1").TextContent);
        Assert.Equal("10/11（日）", page.Find(".tna-today").TextContent.Trim());
    }
}
