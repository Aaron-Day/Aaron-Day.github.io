namespace MovieLibrary.Web.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that reports itself Skipped, naming what to install, when <c>node</c> is not on
/// PATH. Same mechanism as the A11y project's BrowserFactAttribute: xunit v2 reads <c>Skip</c> off the live
/// attribute during discovery, <c>Assert.Skip</c> is v3, and a thrown <c>SkipException</c> is reported Failed.
/// A fresh clone with no node must still exit 0.
/// </summary>
public sealed class NodeFactAttribute : FactAttribute
{
    private string? parked;

    public override string? Skip
    {
        get => parked ?? (Node.Path is null ? "node is not on PATH; install Node.js (any current LTS) to run the script specs." : null);
        set => parked = value;
    }
}

internal static class Node
{
    /// <summary>The full path of the first <c>node</c> executable on PATH, or null.</summary>
    public static readonly string? Path = Find();

    private static string? Find()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "node.exe" } : new[] { "node" };

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = System.IO.Path.Combine(directory.Trim('"'), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is not a reason to fail discovery.
                }
            }
        }

        return null;
    }
}
