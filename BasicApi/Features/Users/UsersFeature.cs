namespace BasicApi.Features.Users;

public static class UsersFeature
{
    public static IServiceCollection AddUsersFeature(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IPrivacyService, PrivacyService>();
        return services;
    }
}
