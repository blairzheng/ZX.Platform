using System.Net;
using GatewayHeaders = ZX.Platform.Gateway.Extensions.ForwardedHeadersExtensions;
using IdentityHeaders = ZX.Platform.Identity.Api.Extensions.ForwardedHeadersExtensions;

namespace ZX.Platform.Identity.AuthChecks;

internal static class ForwardedHeadersChecks
{
    public static async Task RunAsync()
    {
        foreach (var register in new Action<IServiceCollection, IConfiguration>[]
        {
            (services, config) => GatewayHeaders.AddTrustedForwardedHeaders(services, config),
            (services, config) => IdentityHeaders.AddTrustedForwardedHeaders(services, config)
        })
        {
            foreach (var trusted in new[] { true, false })
            {
                var builder = WebApplication.CreateBuilder();
                builder.Logging.ClearProviders();
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:KnownProxies:0"] = trusted ? "127.0.0.1" : "192.0.2.1"
                });
                register(builder.Services, builder.Configuration);
                builder.Services.AddHttpsRedirection(options => options.HttpsPort = 8443);
                await using var app = builder.Build();
                app.UseForwardedHeaders();
                app.UseHttpsRedirection();
                app.MapGet("/", (HttpContext context) =>
                    $"{context.Request.Scheme}|{context.Connection.RemoteIpAddress}");
                await app.StartAsync();
                using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = new Uri(app.Urls.Single())
                };
                using var request = new HttpRequestMessage(HttpMethod.Get, "/");
                request.Headers.Add("X-Forwarded-For", "203.0.113.10");
                request.Headers.Add("X-Forwarded-Proto", "https");
                using var response = await client.SendAsync(request);
                if (trusted)
                {
                    Check.That(response.StatusCode == HttpStatusCode.OK &&
                        await response.Content.ReadAsStringAsync() == "https|203.0.113.10",
                        "Trusted TLS termination must restore scheme and client IP without a redirect.");
                }
                else
                {
                    Check.That(response.StatusCode == HttpStatusCode.TemporaryRedirect &&
                        response.Headers.Location?.Scheme == "https" && response.Headers.Location.Port == 8443,
                        "An untrusted sender must not bypass HTTPS redirection using forged forwarding headers.");
                }
                using var direct = await client.GetAsync("/");
                Check.That(direct.StatusCode == HttpStatusCode.TemporaryRedirect,
                    "HTTP without forwarding headers must still redirect to HTTPS.");
                await app.StopAsync();
            }
        }
        Console.WriteLine("PASS Gateway/Identity trusted HTTPS forwarding and untrusted header rejection");
    }
}
