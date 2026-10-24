using System.Runtime.InteropServices;

namespace MovieLibrary.Core.Tests;

public class SanityTests
{
    [Fact]
    public void TestProjectRunsOnDotNet10()
        => Assert.StartsWith(".NET 10", RuntimeInformation.FrameworkDescription);
}
