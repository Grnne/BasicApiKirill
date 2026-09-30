using BasicApi.Services.Events;
using Microsoft.Extensions.Options;

namespace BasicApi.Features.Push;

public static class PushFeature
{
    public static IServiceCollection AddPushFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PushOptions>(configuration.GetSection(PushOptions.Section));
        services.AddScoped<IPushService, PushService>();
        services.AddSingleton(sp => new PushQueue(
            sp.GetRequiredService<IOptions<PushOptions>>().Value.IsConfigured, sp.GetRequiredService<ILogger<PushQueue>>()));
        services.AddHttpClient<IPushTransport, WebPushTransport>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<PushSender>();
        services.AddHostedService(sp => sp.GetRequiredService<PushSender>());
        return services;
    }
}
