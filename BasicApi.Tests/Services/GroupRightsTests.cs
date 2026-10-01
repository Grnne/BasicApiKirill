using BasicApi.Models.Dto.Chat;
using BasicApi.Services;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Tests.TestDoubles;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Services;

/// <summary>Group roles and permissions.</summary>
public class GroupRightsTests
{
    private static string Patch(PermissionsPatchDto patch) => GroupRights.WritePatch(patch)!;

    [Fact]
    public void Owner_MayDoEverything_WhateverTheOverrides()
    {
        var owner = GroupRights.Effective(ChatRoles.Owner, null, Patch(new() { SendMessages = false }));

        Assert.True(owner is { SendMessages: true, RemoveMembers: true, DeleteMessages: true, AddAdmins: true });
    }

    [Fact]
    public void Admin_MayDoEverythingButAddAdmins_UnlessTheOwnerSaysOtherwise()
    {
        var admin = GroupRights.Effective(ChatRoles.Admin, null, null);
        Assert.True(admin is { RemoveMembers: true, DeleteMessages: true, ChangeInfo: true, AddAdmins: false });

        var trusted = GroupRights.Effective(ChatRoles.Admin, null, Patch(new() { AddAdmins = true, DeleteMessages = false }));
        Assert.True(trusted is { AddAdmins: true, DeleteMessages: false });
    }

    [Fact]
    public void Member_FollowsTheGroupDefaults_ThenTheirOwnOverrides_ButNeverGetsAdminPermissions()
    {
        Assert.True(GroupRights.Effective(ChatRoles.Member, null, null) is
            { SendMessages: true, SendMedia: true, AddMembers: true, ChangeInfo: false, RemoveMembers: false });

        var readOnlyGroup = GroupRights.WriteSettings(new GroupSettings { MemberPermissions = new() { SendMessages = false } });
        Assert.False(GroupRights.Effective(ChatRoles.Member, readOnlyGroup, null).SendMessages);
        Assert.True(GroupRights.Effective(ChatRoles.Member, readOnlyGroup, Patch(new() { SendMessages = true })).SendMessages);

        // Stored admin overrides of a demoted admin do not carry over.
        var sneaky = GroupRights.Effective(ChatRoles.Member, null, Patch(new() { DeleteMessages = true, AddAdmins = true }));
        Assert.True(sneaky is { DeleteMessages: false, AddAdmins: false, RemoveMembers: false });
    }

    [Fact]
    public void NoOverrides_IsStoredAsNothing()
    {
        Assert.Null(GroupRights.WritePatch(new PermissionsPatchDto()));
        Assert.Null(GroupRights.WritePatch(null));
        Assert.Equal("{\"sendMessages\":false}", GroupRights.WritePatch(new PermissionsPatchDto { SendMessages = false }));
    }
}

/// <summary>What the policy lets each role do in a group.</summary>
public class GroupPolicyTests
{
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly ChatPolicy _policy;
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly ChatMember _owner, _admin, _member, _otherMember, _otherAdmin;

    public GroupPolicyTests()
    {
        _owner = Member(ChatRoles.Owner);
        _admin = Member(ChatRoles.Admin);
        _otherAdmin = Member(ChatRoles.Admin);
        _member = Member(ChatRoles.Member);
        _otherMember = Member(ChatRoles.Member);
        _policy = new ChatPolicy(new MembershipService(_chatRepoMock.Object));
    }

    private ChatMember Member(string role, string? overrides = null)
    {
        var member = new ChatMember
        {
            ChatId = _chatId, UserId = Guid.NewGuid(), ChatType = ChatTypes.Group, Role = role, PermissionsJson = overrides
        };
        _chatRepoMock.Setup(r => r.GetMemberAsync(_chatId, member.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        _chatRepoMock.Setup(r => r.IsMemberAsync(_chatId, member.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return member;
    }

    private async Task<bool> Can(ChatMember actor, GroupAction action, ChatMember? target = null) =>
        (await _policy.CanManageAsync(actor.UserId, _chatId, action, target)).Allowed;

    [Fact]
    public async Task Media_NeedsBothSendingRights()
    {
        var noMedia = Member(ChatRoles.Member, """{"sendMedia":false}""");
        var readOnly = Member(ChatRoles.Member, """{"sendMessages":false,"sendMedia":true}""");

        Assert.True((await _policy.CanPostMediaAsync(_member.UserId, _chatId)).Allowed);
        Assert.False((await _policy.CanPostMediaAsync(noMedia.UserId, _chatId)).Allowed);
        Assert.True((await _policy.CanPostAsync(noMedia.UserId, _chatId)).Allowed);
        Assert.False((await _policy.CanPostMediaAsync(readOnly.UserId, _chatId)).Allowed);
        Assert.Equal(ChatPolicy.NotAMemberCode, (await _policy.CanPostMediaAsync(Guid.NewGuid(), _chatId)).Code);
    }

    [Fact]
    public async Task RemovingMembers_OwnerAnyoneButThemselves_AdminOnlyMembers_MemberNobody()
    {
        Assert.True(await Can(_owner, GroupAction.RemoveMember, _admin));
        Assert.True(await Can(_owner, GroupAction.RemoveMember, _member));
        Assert.False(await Can(_owner, GroupAction.RemoveMember, _owner));
        Assert.True(await Can(_admin, GroupAction.RemoveMember, _member));
        Assert.False(await Can(_admin, GroupAction.RemoveMember, _otherAdmin));
        Assert.False(await Can(_admin, GroupAction.RemoveMember, _owner));
        Assert.False(await Can(_member, GroupAction.RemoveMember, _otherMember));
    }

    [Fact]
    public async Task Roles_AdminsAreMadeWithAddAdmins_UnmadeAndHandedOverByTheOwner()
    {
        Assert.True(await Can(_owner, GroupAction.PromoteToAdmin, _member));
        Assert.False(await Can(_admin, GroupAction.PromoteToAdmin, _member));
        Assert.True(await Can(Member(ChatRoles.Admin, GroupRights.WritePatch(new() { AddAdmins = true })),
            GroupAction.PromoteToAdmin, _member));

        Assert.True(await Can(_owner, GroupAction.DemoteAdmin, _admin));
        Assert.False(await Can(_admin, GroupAction.DemoteAdmin, _otherAdmin));

        Assert.True(await Can(_owner, GroupAction.TransferOwnership, _member));
        Assert.False(await Can(_admin, GroupAction.TransferOwnership, _member));
        Assert.True(await Can(_owner, GroupAction.DeleteGroup));
        Assert.False(await Can(_admin, GroupAction.DeleteGroup));
    }

    [Fact]
    public async Task Restricting_AdminsWithRemoveMembersRestrictMembers_OnlyTheOwnerRestrictsAdmins()
    {
        Assert.True(await Can(_admin, GroupAction.RestrictMember, _member));
        Assert.True(await Can(_admin, GroupAction.ChangeMemberDefaults));
        Assert.False(await Can(_admin, GroupAction.RestrictAdmin, _otherAdmin));
        Assert.True(await Can(_owner, GroupAction.RestrictAdmin, _admin));
        Assert.False(await Can(_member, GroupAction.RestrictMember, _otherMember));
        Assert.False(await Can(_member, GroupAction.ChangeMemberDefaults));
        Assert.False(await Can(_member, GroupAction.ViewAudit));
        Assert.True(await Can(_admin, GroupAction.ViewAudit));
    }

    [Fact]
    public async Task Posting_InAGroup_NeedsSendMessages()
    {
        var muted = Member(ChatRoles.Member, GroupRights.WritePatch(new() { SendMessages = false }));

        var decision = await _policy.CanPostAsync(muted.UserId, _chatId);

        Assert.Equal((false, "PERMISSION_DENIED"), (decision.Allowed, decision.Code));
        Assert.True((await _policy.CanPostAsync(_member.UserId, _chatId)).Allowed);
        Assert.True((await _policy.CanReadAsync(muted.UserId, _chatId)).Allowed);
    }

    [Fact]
    public async Task DeletingOthersMessages_AdminsAtAnyTime_MembersNever()
    {
        var old = new MessageWithSender
        {
            ChatId = _chatId, SenderId = _member.UserId, Type = MessageTypes.Text, CreatedAt = DateTime.UtcNow.AddDays(-30)
        };
        var system = new MessageWithSender
        {
            ChatId = _chatId, SenderId = _member.UserId, Type = MessageTypes.System, CreatedAt = DateTime.UtcNow
        };

        Assert.True((await _policy.CanDeleteForEveryoneAsync(_admin.UserId, old)).Allowed);
        Assert.False((await _policy.CanDeleteForEveryoneAsync(_otherMember.UserId, old)).Allowed);
        Assert.Equal("DELETE_WINDOW_EXPIRED", (await _policy.CanDeleteForEveryoneAsync(_member.UserId, old)).Code);
        // Its author may not remove a system message; an admin may.
        Assert.False((await _policy.CanDeleteForEveryoneAsync(_member.UserId, system)).Allowed);
        Assert.True((await _policy.CanDeleteForEveryoneAsync(_admin.UserId, system)).Allowed);
    }
}
