namespace Tna.Frontend;

using MudBlazor;

// 配色はほかの業務アプリと同じ青を基調にする。最初の表示を軽くするため、フォントは落とさずに端末にある日本語のフォントを使う
internal static class AppTheme
{
    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Colors.Blue.Darken3,
            PrimaryDarken = Colors.Blue.Darken4,
            PrimaryLighten = Colors.Blue.Darken2,
            Secondary = Colors.LightBlue.Darken2,
            AppbarBackground = Colors.Blue.Darken3,
            AppbarText = Colors.Shades.White,
            DrawerBackground = Colors.Gray.Lighten4,
            DrawerText = Colors.Gray.Darken4,
            Background = Colors.Gray.Lighten5,
            Surface = Colors.Shades.White
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["system-ui", "-apple-system", "Hiragino Sans", "Hiragino Kaku Gothic ProN", "BIZ UDPGothic", "Meiryo", "sans-serif"]
            }
        }
    };
}
