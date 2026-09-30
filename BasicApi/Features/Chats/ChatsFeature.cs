namespace BasicApi.Features.Chats;

public static class ChatsFeature
{
    public static IServiceCollection AddChatsFeature(this IServiceCollection services)
    {
        services.AddScoped<IChatService, ChatService>();
        return services;
    }
}
