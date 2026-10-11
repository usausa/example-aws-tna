namespace Tna.Frontend.Layout;

using Microsoft.AspNetCore.Components.Routing;

using MudBlazor;

internal sealed record NavItem(string Title, string Href, string Icon, NavLinkMatch Match);

// スマホは簡単な機能だけを下のタブに出し、タブにない項目は「その他」にまとめる。PC はすべての機能を左のメニューに出す。
// まだ作っていない画面の項目は出さない
internal static class NavItems
{
    private static readonly NavItem Home = new("ホーム", String.Empty, Icons.Material.Filled.Home, NavLinkMatch.All);

    // スマホの下のタブ
    public static IReadOnlyList<NavItem> Tabs { get; } =
    [
        Home,
        new("その他", "more", Icons.Material.Filled.MoreHoriz, NavLinkMatch.Prefix)
    ];

    // PC の左のメニュー
    public static IReadOnlyList<NavItem> Menu { get; } = [Home];
}
