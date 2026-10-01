namespace BasicApi.Features.Media;

public static class MediaFeature
{
    public static IServiceCollection AddMediaFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.Section));
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddSingleton<MediaCleanup>();
        services.AddHostedService(sp => sp.GetRequiredService<MediaCleanup>());
        return services;
    }
}
