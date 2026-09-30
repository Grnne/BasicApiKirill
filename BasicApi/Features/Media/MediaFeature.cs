namespace BasicApi.Features.Media;

public static class MediaFeature
{
    public static IServiceCollection AddMediaFeature(this IServiceCollection services, IConfiguration configuration)
    {
        // Files live in S3-compatible storage; without Storage:Endpoint media is off (503).
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.Section));
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddSingleton<MediaCleanup>();
        services.AddHostedService(sp => sp.GetRequiredService<MediaCleanup>());
        return services;
    }
}
