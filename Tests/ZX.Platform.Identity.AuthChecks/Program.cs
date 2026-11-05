using ZX.Platform.Identity.AuthChecks;

if (args.Contains("--assistant-query-only"))
{
    await AssistantQueryChecks.RunAsync();
    return;
}

if (args.Contains("--menu-only"))
{
    await MenuChecks.RunAsync();
    return;
}

DevelopmentMigrationChecks.Run();
if (args.Contains("--development-migrations-only")) return;

if (args is ["--oracle19-sql", var outputPath])
{
    Oracle19Checks.Run(outputPath);
    return;
}
Oracle19Checks.Run();
if (args.Contains("--oracle19-only")) return;

await SbuScopeChecks.RunAsync();
if (args.Contains("--sbu-scope-only")) return;

await ExternalOracleChecks.RunAsync();
if (args.Contains("--external-oracle-connectivity"))
{
    try
    {
        await ExternalOracleChecks.RunConnectivityAsync();
    }
    catch (InvalidOperationException exception)
    {
        Console.Error.WriteLine(exception.Message);
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Contains("--external-oracle-only")) return;

await ForwardedHeadersChecks.RunAsync();
if (args.Contains("--forwarded-headers-only")) return;

await SessionChecks.RunAsync();
await AuthFlowChecks.RunAsync();
await LdapChecks.RunAsync();
if (args.Contains("--ldap-only")) return;
await AuthorizationChecks.RunAsync();
await MenuChecks.RunAsync();
await HttpChecks.RunAsync();
await OracleChecks.RunAsync();
Console.WriteLine("All authentication checks passed.");
