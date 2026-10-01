namespace BasicApi.Features.Auth;

public static class AuthFeature
{
    public static IServiceCollection AddAuthFeature(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IJwtService, JwtService>();
        return services;
    }
}
