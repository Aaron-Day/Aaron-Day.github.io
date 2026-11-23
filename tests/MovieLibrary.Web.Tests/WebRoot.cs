namespace MovieLibrary.Web.Tests;

/// <summary>Finds the web project's files by walking up to the solution file, as the Core tests do for wwwroot.</summary>
internal static class WebRoot
{
    private static readonly Lazy<string> Solution = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MovieLibrary.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);   // a test that silently cannot find the repo proves nothing
        return dir.FullName;
    });

    private static readonly Lazy<string> Project =
        new(() => Path.Combine(Solution.Value, "src", "MovieLibrary.Web"));

    public static string Read(params string[] relativePath) =>
        File.ReadAllText(Path.Combine(new[] { Project.Value }.Concat(relativePath).ToArray()));

    /// <summary>
    /// A file in another project under <c>src/</c>. Only for contracts about what the web payload drags in
    /// with it: this project's one ProjectReference is cheap because of what is NOT in the project it points
    /// at, which is a fact about that project's file and not about this one.
    /// </summary>
    public static string ReadSibling(string project, params string[] relativePath) =>
        File.ReadAllText(Path.Combine(new[] { Solution.Value, "src", project }.Concat(relativePath).ToArray()));

    /// <summary>The full path of a file in another project under <c>src/</c>, for handing to a process.</summary>
    public static string SiblingPath(string project, params string[] relativePath) =>
        Path.Combine(new[] { Solution.Value, "src", project }.Concat(relativePath).ToArray());

    /// <summary>The full path of a file under the repository root.</summary>
    public static string RepoPath(params string[] relativePath) =>
        Path.Combine(new[] { Solution.Value }.Concat(relativePath).ToArray());

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

    /// <summary>
    /// The paths of every hand-written file under the editor project matching the patterns, reached the way
    /// ReadSibling reaches a sibling. Build output is excluded for the same reason as in Files.
    /// </summary>
    public static IEnumerable<string> EditorFiles(params string[] patterns)
    {
        var root = EditorRoot.Value;
        return patterns
            .SelectMany(p => Directory.EnumerateFiles(root, p, SearchOption.AllDirectories))
            .Where(f => !Segments(f).Contains("obj") && !Segments(f).Contains("bin"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static readonly Lazy<string> EditorRoot = new(() => Path.Combine(Solution.Value, "src", "MovieLibrary.Web.Editor"));

    /// <summary>The prefix an editor file's path carries in <see cref="FilesWithEditor"/>, so a failure names its project.</summary>
    public const string EditorPrefix = "MovieLibrary.Web.Editor/";

    /// <summary>
    /// <see cref="Files"/> plus the same patterns under the editor project, in the same (path, text) shape.
    /// Web paths stay relative to the web project and editor paths carry <see cref="EditorPrefix"/>, so a
    /// failing rule says which project to open. EditorFiles is a different shape on purpose (bare absolute
    /// paths, for the tests that open the files themselves) and keeps its callers.
    /// </summary>
    public static IEnumerable<(string Path, string Text)> FilesWithEditor(params string[] patterns) =>
        Files(patterns)
            .Concat(EditorFiles(patterns).Select(f =>
                (EditorPrefix + Path.GetRelativePath(EditorRoot.Value, f)
                                    .Replace(Path.DirectorySeparatorChar, '/'),
                 File.ReadAllText(f))))
            .ToList();

    private static string[] Segments(string path) =>
        path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
}
