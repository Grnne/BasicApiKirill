using System.Text.Json;
using BasicApi.Models.Dto.Message;

namespace BasicApi.Services;

/// <summary>
/// System messages of a group: what happened, stored in <c>messages.content</c>, and the same in
/// words for clients that do not know the action. The words are Russian: the product's language.
/// </summary>
public static class SystemMessages
{
    public const string GroupCreated = "group_created";
    public const string TitleChanged = "title_changed";
    public const string MembersAdded = "members_added";
    public const string MemberRemoved = "member_removed";
    public const string MemberLeft = "member_left";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static (MessageActionDto Action, string Text) Created(string title) =>
        (new MessageActionDto { Type = GroupCreated, Title = title }, $"Создана группа «{title}»");

    public static (MessageActionDto Action, string Text) Renamed(string title) =>
        (new MessageActionDto { Type = TitleChanged, Title = title }, $"Название группы изменено на «{title}»");

    public static (MessageActionDto Action, string Text) Added(IReadOnlyList<(Guid Id, string Name)> users) =>
        (new MessageActionDto { Type = MembersAdded, UserIds = [.. users.Select(u => u.Id)] },
            (users.Count == 1 ? "Добавлен участник: " : "Добавлены участники: ") + string.Join(", ", users.Select(u => u.Name)));

    public static (MessageActionDto Action, string Text) Removed(Guid userId, string name) =>
        (new MessageActionDto { Type = MemberRemoved, UserIds = [userId] }, $"Участник {name} исключён из группы");

    public static (MessageActionDto Action, string Text) Left(Guid userId, string name) =>
        (new MessageActionDto { Type = MemberLeft, UserIds = [userId] }, $"Участник {name} покинул группу");

    public static string Write(MessageActionDto action) => JsonSerializer.Serialize(action, Json);

    public static MessageActionDto? Read(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<MessageActionDto>(json, Json);
}
