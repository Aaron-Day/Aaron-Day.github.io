namespace MovieLibrary.Enrich.Run;

/// <summary>What a run decided. The CLI turns <see cref="ExitCode"/> into the process exit code; Task 18's deploy gate
/// reads <see cref="Deployable"/>.</summary>
/// <param name="ExitCode">0 when the catalogue was written, 1 on any fatal error.</param>
/// <param name="StoppedOnBudget">The request budget ran out before every lookup finished.</param>
/// <param name="Deployable">
/// Exit 0 <b>and</b> a complete, real catalogue. False for a budget-stopped run (written for local preview only), for a
/// fixture run (synthetic data), and for every failure.
/// </param>
public sealed record RunResult(int ExitCode, bool StoppedOnBudget, bool Deployable);
