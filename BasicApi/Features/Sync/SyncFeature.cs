namespace BasicApi.Features.Sync;

public static class SyncFeature
{
    public static IServiceCollection AddSyncFeature(this IServiceCollection services)
    {
        services.AddScoped<ISyncService, SyncService>();
        return services;
    }
}
