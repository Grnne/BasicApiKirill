namespace BasicApi.Features.Groups;

public static class GroupsFeature
{
    public static IServiceCollection AddGroupsFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GroupOptions>(configuration.GetSection(GroupOptions.Section));
        services.AddScoped<IGroupService, GroupService>();
        return services;
    }
}
