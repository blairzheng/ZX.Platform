using ZX.Platform.Identity.AuthChecks;

await SessionChecks.RunAsync();
await AuthFlowChecks.RunAsync();
await AuthorizationChecks.RunAsync();
await MenuChecks.RunAsync();
await HttpChecks.RunAsync();
Console.WriteLine("All authentication checks passed.");
