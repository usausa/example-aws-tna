namespace Tna.Generator;

internal static class RepositoryRoot
{
    // Tna.slnx のあるフォルダ。道具の置き場所から上へ探し、なければ今のフォルダから上へ探す
    public static string Find()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Tna.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new InvalidOperationException("Tna.slnx is not found. Run the tool inside the repository.");
    }
}
