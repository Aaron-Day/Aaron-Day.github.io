using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MovieLibrary.Web.Services;

namespace MovieLibrary.Web.Tests;

internal static class AnnouncerTestExtensions
{
    /// <summary>
    /// Registers the announcer the way Program.cs does (one instance for the session) but on a clock the test
    /// owns, and returns that clock. Anything that renders MainLayout or App needs this.
    /// </summary>
    public static ManualTimeProvider AddAnnouncer(this BunitContext context)
    {
        var clock = new ManualTimeProvider();
        context.Services.AddSingleton(clock);
        context.Services.AddSingleton(new Announcer(clock));
        return clock;
    }
}
