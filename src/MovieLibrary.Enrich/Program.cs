using MovieLibrary.Enrich.Config;
using MovieLibrary.Enrich.Run;

// A shell. The work is in EnrichCli and EnrichRun, where a test can reach it.
//
// Ctrl+C asks the run to stop instead of killing it, so the finally that saves the TMDB cache gets to run: a full seed
// takes tens of minutes and every response in it was paid for. A second Ctrl+C kills the process as usual.
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = !cancel.IsCancellationRequested;
    cancel.Cancel();
};

return await EnrichCli.MainAsync(args, EnrichOptions.ReadEnvironment(), Console.Out, Console.Error, cancel.Token);
