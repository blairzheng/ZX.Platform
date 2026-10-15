using ZX.Platform.Identity.AuthChecks;

await SessionChecks.RunAsync();
await AuthFlowChecks.RunAsync();
await HttpChecks.RunAsync();
Console.WriteLine("All authentication checks passed.");
