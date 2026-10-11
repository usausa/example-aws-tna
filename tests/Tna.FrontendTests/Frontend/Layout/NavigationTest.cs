namespace Tna.Frontend.Layout;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

// スマホの下のタブと PC のメニューは、作った画面だけを指す。「その他」はスマホのタブだけに置く
public sealed class NavigationTest : BunitContext
{
    public NavigationTest()
    {
        this.AddMudBlazor();
    }

    [Fact]
    public void ItemsAreTheScreensThatAreMade()
    {
        Assert.Equal(["ホーム", "その他"], NavItems.Tabs.Select(static x => x.Title));
        Assert.Equal(["ホーム"], NavItems.Menu.Select(static x => x.Title));
    }

    [Fact]
    public void BottomTabsLinkToTheScreens()
    {
        var nav = Render<BottomNav>();

        var links = nav.FindAll("a.tna-bottom-nav-item");
        Assert.Equal(["ホーム", "その他"], links.Select(static x => x.TextContent.Trim()));
        Assert.Equal([String.Empty, "more"], links.Select(static x => x.GetAttribute("href")));
    }

    // いまの画面のタブだけが選ばれている
    [Theory]
    [InlineData("", "ホーム")]
    [InlineData("more", "その他")]
    public void BottomTabOfTheCurrentScreenIsActive(string path, string title)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);

        var nav = Render<BottomNav>();

        var active = nav.FindAll("a.tna-bottom-nav-item.active");
        Assert.Equal([title], active.Select(static x => x.TextContent.Trim()));
    }

    [Fact]
    public void MenuLinksToTheScreens()
    {
        var menu = Render<NavMenu>();

        var links = menu.FindAll("a");
        Assert.Equal(["ホーム"], links.Select(static x => x.TextContent.Trim()));
        Assert.Equal([String.Empty], links.Select(static x => x.GetAttribute("href")));
    }
}
