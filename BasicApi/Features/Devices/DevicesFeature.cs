namespace BasicApi.Features.Devices;

public static class DevicesFeature
{
    public static IServiceCollection AddDevicesFeature(this IServiceCollection services)
    {
        services.AddScoped<IDeviceService, DeviceService>();
        return services;
    }
}
