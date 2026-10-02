namespace BasicApi.Features.Auth;

public static class AuthFeature
{
    public static IServiceCollection AddAuthFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RegistrationOptions>(configuration.GetSection(RegistrationOptions.Section));
        services.AddScoped<AuthService>();
        services.AddScoped<InviteService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IJwtService, JwtService>();
        return services;
    }
}
