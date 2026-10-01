using BasicApi.Features.Messages;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using BasicApi.Tests.TestDoubles;
using Microsoft.Extensions.Options;
using Moq;

namespace BasicApi.Tests.Features.Messages;

public class MessageEditDeleteTests
{
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IMessageRepository> _msgRepoMock = new();
    private readonly Mock<IChatEventPublisher> _eventsMock = new();
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly Guid _author = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _messageId = Guid.NewGuid();

    public MessageEditDeleteTests()
    {
        _chatRepoMock.WithMembersFromIsMember();
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(_chatId, It.Is<Guid>(u => u == _author || u == _other), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _chatRepoMock
            .Setup(r => r.GetMemberIdsAsync(_chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_author, _other]);
        _msgRepoMock
            .Setup(r => r.EditTextAsync(_messageId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string text, string? _, DateTime editedAt, CancellationToken _) =>
                Stored(text: text, editedAt: editedAt));
    }

    private MessageService Service(MessageOptions? options = null)
    {
        var membership = new MembershipService(_chatRepoMock.Object);
        var policy = new ChatPolicy(membership, Options.Create(options ?? new MessageOptions()));
        return new MessageService(new FakeDbSession(), _msgRepoMock.Object, membership, policy, _eventsMock.Object, Mock.Of<IDraftRepository>(), Mock.Of<IGroupRepository>(), Mock.Of<IAttachmentRepository>());
    }

    private MessageWithSender Stored(
        string text = "original", TimeSpan? age = null, DateTime? deletedAt = null, DateTime? editedAt = null) => new()
    {
        Id = _messageId,
        ChatId = _chatId,
        SenderId = _author,
        SenderName = "Author",
        Text = text,
        CreatedAt = DateTime.UtcNow - (age ?? TimeSpan.FromMinutes(5)),
        Seq = 3,
        DeletedAt = deletedAt,
        EditedAt = editedAt
    };

    private void Existing(MessageWithSender message) =>
        _msgRepoMock
            .Setup(r => r.GetAsync(_chatId, _messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

    [Fact]
    public async Task Edit_ByAuthor_StoresText_AndAnnouncesTheWholeMessageToAllMembers()
    {
        Existing(Stored());

        var edited = await Service().EditAsync(_chatId, _author, _messageId, "  fixed  ");

        Assert.Equal("fixed", edited.Text);
        Assert.NotNull(edited.EditedAt);
        _msgRepoMock.Verify(r => r.EditTextAsync(_messageId, "fixed", null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.Verify(e => e.MessageUpdatedAsync(edited,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(_author) && ids.Contains(_other)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Edit_WithTheSameText_ChangesNothing_AndSendsNothing()
    {
        Existing(Stored(text: "same"));

        var result = await Service().EditAsync(_chatId, _author, _messageId, " same ");

        Assert.Equal("same", result.Text);
        Assert.Null(result.EditedAt);
        _msgRepoMock.Verify(r => r.EditTextAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Edit_ByAnotherMember_IsForbidden()
    {
        Existing(Stored());

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service().EditAsync(_chatId, _other, _messageId, "mine now"));

        Assert.Equal(ChatPolicy.NotMessageAuthorCode, ex.ErrorCode);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Edit_AfterTheWindow_IsForbidden()
    {
        Existing(Stored(age: TimeSpan.FromHours(49)));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service().EditAsync(_chatId, _author, _messageId, "too late"));

        Assert.Equal(ChatPolicy.EditWindowExpiredCode, ex.ErrorCode);
    }

    [Fact]
    public async Task Edit_WithoutAWindow_IsAllowedAnyTime()
    {
        Existing(Stored(age: TimeSpan.FromDays(400)));

        var edited = await Service(new MessageOptions { EditWindowHours = 0 })
            .EditAsync(_chatId, _author, _messageId, "a year later");

        Assert.Equal("a year later", edited.Text);
    }

    [Fact]
    public async Task Edit_DeletedMessage_IsNotFound()
    {
        Existing(Stored(deletedAt: DateTime.UtcNow));

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().EditAsync(_chatId, _author, _messageId, "revive"));

        Assert.Equal("MESSAGE_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task Edit_MessageOfAnotherChat_IsNotFound()
    {
        // GetAsync is scoped to the chat: a message of another chat is simply not found.
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().EditAsync(_chatId, _author, Guid.NewGuid(), "text"));

        Assert.Equal("MESSAGE_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task Edit_EmptyText_OfATextMessage_IsRejected_BeforeAnythingIsWritten()
    {
        // Emptiness depends on the message: a media message may lose its caption, a text may not.
        Existing(Stored());

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            Service().EditAsync(_chatId, _author, _messageId, "   "));

        Assert.Equal(MessageText.EmptyCode, ex.ErrorCode);
        _msgRepoMock.Verify(r => r.EditTextAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_EmptyText_OfAMediaMessage_RemovesTheCaption()
    {
        var media = Stored("caption");
        media.Type = MessageTypes.Media;
        Existing(media);
        _msgRepoMock
            .Setup(r => r.EditTextAsync(_messageId, "", null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stored(""));

        var edited = await Service().EditAsync(_chatId, _author, _messageId, " ");

        Assert.Equal("", edited.Text);
    }

    [Fact]
    public async Task Edit_ByNonMember_IsForbidden()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service().EditAsync(_chatId, Guid.NewGuid(), _messageId, "text"));

        Assert.Equal(ChatPolicy.NotAMemberCode, ex.ErrorCode);
        _msgRepoMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteForEveryone_ByAuthor_MakesATombstone_AndTellsAllMembers()
    {
        Existing(Stored());
        _msgRepoMock
            .Setup(r => r.DeleteForEveryoneAsync(_messageId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Service().DeleteAsync(_chatId, _author, _messageId, forEveryone: true);

        _eventsMock.Verify(e => e.MessageDeletedAsync(
            It.Is<MessageDeletedDto>(d => d.ChatId == _chatId && d.MessageId == _messageId && d.Seq == 3 && d.ForEveryone),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        _msgRepoMock.Verify(r => r.HideAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteForEveryone_ByAnotherMember_IsForbidden()
    {
        Existing(Stored());

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service().DeleteAsync(_chatId, _other, _messageId, forEveryone: true));

        Assert.Equal(ChatPolicy.NotMessageAuthorCode, ex.ErrorCode);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteForEveryone_AfterTheWindow_IsForbidden()
    {
        Existing(Stored(age: TimeSpan.FromHours(3)));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(new MessageOptions { DeleteWindowHours = 2 }).DeleteAsync(_chatId, _author, _messageId, forEveryone: true));

        Assert.Equal(ChatPolicy.DeleteWindowExpiredCode, ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteForEveryone_Repeated_IsQuiet()
    {
        Existing(Stored(deletedAt: DateTime.UtcNow));

        await Service().DeleteAsync(_chatId, _author, _messageId, forEveryone: true);

        _msgRepoMock.Verify(r => r.DeleteForEveryoneAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteForEveryone_LostARaceWithAnotherDelete_SendsNothing()
    {
        Existing(Stored());
        _msgRepoMock
            .Setup(r => r.DeleteForEveryoneAsync(_messageId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Service().DeleteAsync(_chatId, _author, _messageId, forEveryone: true);

        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteForMe_AnyMessage_HidesItForThatUserOnly()
    {
        Existing(Stored());
        _msgRepoMock.Setup(r => r.HideAsync(_other, _messageId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Service().DeleteAsync(_chatId, _other, _messageId, forEveryone: false);

        _msgRepoMock.Verify(r => r.DeleteForEveryoneAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventsMock.Verify(e => e.MessageDeletedAsync(
            It.Is<MessageDeletedDto>(d => d.MessageId == _messageId && !d.ForEveryone),
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(_other)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteForMe_Repeated_SendsNothing()
    {
        Existing(Stored());
        _msgRepoMock.Setup(r => r.HideAsync(_other, _messageId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Service().DeleteAsync(_chatId, _other, _messageId, forEveryone: false);

        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Delete_ByNonMember_IsForbidden()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service().DeleteAsync(_chatId, Guid.NewGuid(), _messageId, forEveryone: false));

        Assert.Equal(ChatPolicy.NotAMemberCode, ex.ErrorCode);
        _msgRepoMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Delete_UnknownMessage_IsNotFound()
    {
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().DeleteAsync(_chatId, _author, Guid.NewGuid(), forEveryone: false));

        Assert.Equal("MESSAGE_NOT_FOUND", ex.ErrorCode);
    }
}
