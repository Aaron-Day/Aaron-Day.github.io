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

    /// <summary>
    /// Every hand-written file under the web project matching the search patterns, as (path relative to the
    /// project, text) pairs. Build output is excluded: obj\ holds generated .cs that would match anything.
    /// </summary>
    public static IEnumerable<(string Path, string Text)> Files(params string[] patterns) =>
        patterns
            .SelectMany(p => Directory.EnumerateFiles(Project.Value, p, SearchOption.AllDirectories))
            .Where(f => !Segments(f).Contains("obj") && !Segments(f).Contains("bin"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => (Path.GetRelativePath(Project.Value, f).Replace(Path.DirectorySeparatorChar, '/'),
                          File.ReadAllText(f)))
            .ToList();

    private static string[] Segments(string path) =>
        path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
}
