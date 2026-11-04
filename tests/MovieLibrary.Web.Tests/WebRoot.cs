namespace MovieLibrary.Web.Tests;

/// <summary>Finds the web project's files by walking up to the solution file, as the Core tests do for wwwroot.</summary>
internal static class WebRoot
{
    private static readonly Lazy<string> Project = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MovieLibrary.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);   // a test that silently cannot find the repo proves nothing
        return Path.Combine(dir.FullName, "src", "MovieLibrary.Web");
    });

    public static string Read(params string[] relativePath) =>
        File.ReadAllText(Path.Combine(new[] { Project.Value }.Concat(relativePath).ToArray()));
}
