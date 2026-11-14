namespace GarageStack.Tests;

/// <summary>Files in the repository the tests run from, found by walking up to GarageStack.slnx.</summary>
internal static class RepositoryFiles
{
    public static string PathOf(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageStack.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }
}
