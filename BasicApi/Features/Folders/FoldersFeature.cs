namespace BasicApi.Features.Folders;

public static class FoldersFeature
{
    public static IServiceCollection AddFoldersFeature(this IServiceCollection services)
    {
        services.AddScoped<IFolderService, FolderService>();
        return services;
    }
}
