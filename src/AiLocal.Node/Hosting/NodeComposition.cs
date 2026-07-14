using System.Net.Security;
using System.Net.Http;
using AiLocal.Core.Configuration;
using AiLocal.Core.Hardware;
using AiLocal.Core.Providers;

namespace AiLocal.Node.Hosting;

/// <summary>Wires the shared services every role needs, incl. the provider fallback chain.</summary>
public static class NodeComposition
{
    public static void AddSharedServices(IServiceCollection services, NodeSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(settings.Providers);
        services.AddSingleton<HostLocator>();
        services.AddSingleton<HostRegistry>();
        services.AddSingleton<RegistrationStatus>();
        services.AddSingleton<LocalRuntimeManager>();
        services.AddSingleton<PairingCoordinator>();
        services.AddTransient<ClusterTokenHandler>();
        services.AddHttpClient();

        // Node-to-node calls: attaches the cluster token, and trusts a
        // self-signed server certificate when a node advertises its TLS
        // endpoint (see TlsCertificateManager/TlsSettings) - the cluster
        // token is the real authentication boundary here, not the cert.
        services.AddHttpClient("cluster")
            .AddHttpMessageHandler<ClusterTokenHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });

        // Explicit, configurable timeouts so a hung local model can't occupy a
        // capacity slot forever, and so a Worker queried directly (no Host in
        // front) still fails cleanly instead of hanging.
        var providerTimeout = TimeSpan.FromSeconds(Math.Max(10, settings.Providers.RequestTimeoutSeconds));
        services.AddHttpClient("anthropic", c => c.Timeout = providerTimeout);
        services.AddHttpClient("gemini", c => c.Timeout = providerTimeout);
        services.AddHttpClient("ollama", c => c.Timeout = providerTimeout);
        services.AddHttpClient("openrouter", c => c.Timeout = providerTimeout);

        // GitHub's API rejects requests with no User-Agent. Long timeout
        // because this same client also streams down the ~100-200MB release
        // exe for self-update (SelfUpdater), not just the small JSON check.
        services.AddHttpClient("github", c =>
        {
            c.Timeout = TimeSpan.FromMinutes(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("AiLocal-SelfUpdater");
        });

        services.AddSingleton(sp =>
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var recommendation = sp.GetRequiredService<LocalModelRecommendation>();
            var credentials = sp.GetRequiredService<PersistentSettingsStore>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("providers");
            var ps = settings.Providers;

            var byName = new Dictionary<string, IChatProvider>(StringComparer.OrdinalIgnoreCase)
            {
                ["anthropic"] = new AnthropicProvider(
                    httpFactory.CreateClient("anthropic"),
                    () => credentials.GetApiKey("anthropic"),
                    ps),

                ["gemini"] = new GeminiProvider(
                    httpFactory.CreateClient("gemini"),
                    () => credentials.GetApiKey("gemini"),
                    ps),

                ["ollama"] = new OllamaProvider(
                    httpFactory.CreateClient("ollama"),
                    ps,
                    recommendation),

                ["openrouter"] = new OpenRouterProvider(
                    httpFactory.CreateClient("openrouter"),
                    () => credentials.GetApiKey("openrouter"),
                    ps),
            };

            return new FallbackChatProvider(byName.Values, ps, msg => logger.LogInformation("{Message}", msg));
        });
    }
}
