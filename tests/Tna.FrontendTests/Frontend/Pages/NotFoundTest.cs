namespace Tna.Frontend.Pages;

using Bunit;

public sealed class NotFoundTest : BunitContext
{
    public NotFoundTest()
    {
        this.AddMudBlazor();
    }

    // MudButton は Href が空だとボタンとして描かれ、押してもホームへ移らない
    [Fact]
    public void HomeButtonIsLinkToHome()
    {
        var page = Render<NotFound>();

        var link = page.Find("a.mud-button-root");
        Assert.Equal("ホームへ", link.TextContent.Trim());
        Assert.Equal("./", link.GetAttribute("href"));
    }
}
