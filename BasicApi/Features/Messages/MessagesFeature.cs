namespace BasicApi.Features.Messages;

public static class MessagesFeature
{
    public static IServiceCollection AddMessagesFeature(this IServiceCollection services)
    {
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IReactionService, ReactionService>();
        services.AddScoped<IReadStateService, ReadStateService>();
        services.AddScoped<IDraftService, DraftService>();
        return services;
    }
}
