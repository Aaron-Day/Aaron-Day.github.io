using MovieLibrary.Enrich.Run;

// A shell. The work is in EnrichCli and EnrichRun, where a test can reach it, and so are the two things this file used
// to do itself: Ctrl+C, and reading the environment.
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => e.Cancel = EnrichCli.RequestStop(cancel);

var env = EnrichCli.TryReadEnvironment(Console.Error);
if (env is null) return 1;

return await EnrichCli.MainAsync(args, env, Console.Out, Console.Error, cancel.Token);
