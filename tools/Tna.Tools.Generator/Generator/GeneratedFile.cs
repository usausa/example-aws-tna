namespace Tna.Generator;

using System.Text;

// 生成したファイルを書く。--verify では、コミットしたファイルが最新かを確かめる
internal static class GeneratedFile
{
    public static bool Write(string root, string relativePath, string content, bool verify)
    {
        var path = Path.Combine(root, relativePath);
        var current = File.Exists(path) ? File.ReadAllText(path) : null;
        if ((current is not null) && String.Equals(current.ReplaceLineEndings("\n"), content.ReplaceLineEndings("\n"), StringComparison.Ordinal))
        {
            Console.WriteLine($"Up to date: {relativePath}");
            return true;
        }

        if (verify)
        {
            Console.Error.WriteLine($"Out of date: {relativePath}. Run: dotnet run --project tools/Tna.Tools.Generator");
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        Console.WriteLine($"Generated: {relativePath}");
        return true;
    }
}
