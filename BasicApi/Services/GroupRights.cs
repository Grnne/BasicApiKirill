using System.Text.Json;
using BasicApi.Models.Dto.Chat;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;

namespace BasicApi.Services;

/// <summary>
/// What a member of a group may do (D8, A6): a set per role, the group's defaults for members and
/// the member's own overrides. The owner may do everything. A member never gets an admin permission
/// (removing members, deleting others' messages, making admins) — that is what making them an admin is for.
/// </summary>
public static class GroupRights
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static GroupPermissionsDto All() => new()
    {
        SendMessages = true, SendMedia = true, AddMembers = true, ChangeInfo = true,
        RemoveMembers = true, DeleteMessages = true, AddAdmins = true
    };

    /// <summary>What members may do in a new group.</summary>
    public static GroupPermissionsDto MemberDefaults() => new()
    {
        SendMessages = true, SendMedia = true, AddMembers = true
    };

    /// <summary>An admin may do everything but make other admins, unless the owner allows it.</summary>
    public static GroupPermissionsDto AdminDefaults()
    {
        var admin = All();
        admin.AddAdmins = false;
        return admin;
    }

    /// <summary>The group's permissions for members: its settings over the instance defaults.</summary>
    public static GroupPermissionsDto MemberPermissions(string? settingsJson) =>
        MembersOnly(Apply(MemberDefaults(), ReadSettings(settingsJson).MemberPermissions));

    /// <summary>What the member may do now.</summary>
    public static GroupPermissionsDto Effective(ChatMember member) =>
        Effective(member.Role, member.SettingsJson, member.PermissionsJson);

    public static GroupPermissionsDto Effective(string role, string? settingsJson, string? overridesJson) => role switch
    {
        ChatRoles.Owner => All(),
        ChatRoles.Admin => Apply(AdminDefaults(), ReadPatch(overridesJson)),
        _ => MembersOnly(Apply(MemberPermissions(settingsJson), ReadPatch(overridesJson)))
    };

    /// <summary>The permissions with the change applied; the original is not touched.</summary>
    public static GroupPermissionsDto Apply(GroupPermissionsDto permissions, PermissionsPatchDto? patch) => new()
    {
        SendMessages = patch?.SendMessages ?? permissions.SendMessages,
        SendMedia = patch?.SendMedia ?? permissions.SendMedia,
        AddMembers = patch?.AddMembers ?? permissions.AddMembers,
        ChangeInfo = patch?.ChangeInfo ?? permissions.ChangeInfo,
        RemoveMembers = patch?.RemoveMembers ?? permissions.RemoveMembers,
        DeleteMessages = patch?.DeleteMessages ?? permissions.DeleteMessages,
        AddAdmins = patch?.AddAdmins ?? permissions.AddAdmins
    };

    /// <summary>A change that touches admin permissions — not allowed for members and group defaults.</summary>
    public static bool TouchesAdminPermissions(PermissionsPatchDto patch) =>
        patch.RemoveMembers is not null || patch.DeleteMessages is not null || patch.AddAdmins is not null;

    /// <summary>Two changes as one: the later wins field by field. Null when nothing is overridden.</summary>
    public static PermissionsPatchDto? Merge(PermissionsPatchDto? earlier, PermissionsPatchDto later)
    {
        var merged = new PermissionsPatchDto
        {
            SendMessages = later.SendMessages ?? earlier?.SendMessages,
            SendMedia = later.SendMedia ?? earlier?.SendMedia,
            AddMembers = later.AddMembers ?? earlier?.AddMembers,
            ChangeInfo = later.ChangeInfo ?? earlier?.ChangeInfo,
            RemoveMembers = later.RemoveMembers ?? earlier?.RemoveMembers,
            DeleteMessages = later.DeleteMessages ?? earlier?.DeleteMessages,
            AddAdmins = later.AddAdmins ?? earlier?.AddAdmins
        };
        return IsEmpty(merged) ? null : merged;
    }

    public static PermissionsPatchDto? ReadPatch(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<PermissionsPatchDto>(json, Json);

    public static string? WritePatch(PermissionsPatchDto? patch) =>
        patch is null || IsEmpty(patch) ? null : JsonSerializer.Serialize(patch, Json);

    public static GroupSettings ReadSettings(string? json) =>
        string.IsNullOrEmpty(json) ? new GroupSettings() : JsonSerializer.Deserialize<GroupSettings>(json, Json) ?? new GroupSettings();

    public static string WriteSettings(GroupSettings settings) => JsonSerializer.Serialize(settings, Json);

    private static bool IsEmpty(PermissionsPatchDto p) =>
        p is { SendMessages: null, SendMedia: null, AddMembers: null, ChangeInfo: null,
               RemoveMembers: null, DeleteMessages: null, AddAdmins: null };

    private static GroupPermissionsDto MembersOnly(GroupPermissionsDto permissions)
    {
        permissions.RemoveMembers = false;
        permissions.DeleteMessages = false;
        permissions.AddAdmins = false;
        return permissions;
    }
}

/// <summary>chats.settings of a group.</summary>
public class GroupSettings
{
    /// <summary>Changes to what members may do by default; null — the instance defaults.</summary>
    public PermissionsPatchDto? MemberPermissions { get; set; }
}
