using BasicApi.Features.Messages;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
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

public class ReactionServiceTests
{
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IReactionRepository> _reactionsMock = new();
    private readonly Mock<IMessageRepository> _messagesMock = new();
    private readonly Mock<IUserRepository> _usersMock = new();
    private readonly Mock<IChatEventPublisher> _eventsMock = new();
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _authorId = Guid.NewGuid();
    private readonly Guid _messageId = Guid.NewGuid();

    public ReactionServiceTests()
    {
        _chatRepoMock.WithMembersFromIsMember();
        _chatRepoMock.Setup(r => r.IsMemberAsync(_chatId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.GetMemberIdsAsync(_chatId, It.IsAny<CancellationToken>())).ReturnsAsync([_userId, Guid.NewGuid()]);
    }

    private ReactionService Service(MessageOptions? options = null)
    {
        var membership = new MembershipService(_chatRepoMock.Object);
        return new ReactionService(new FakeDbSession(), _reactionsMock.Object, membership, new ChatPolicy(membership),
            _eventsMock.Object, _chatRepoMock.Object, _messagesMock.Object, _usersMock.Object, TimeProvider.System,
            Options.Create(options ?? new MessageOptions()));
    }

    private void Changes(bool found, bool changed, string? summary = "[{\"emoji\":\"👍\",\"count\":1}]", Guid author = default)
    {
        var result = new ReactionChange(found, changed, found ? summary : null, author);
        _reactionsMock.Setup(r => r.SetAsync(_chatId, _messageId, _userId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
        _reactionsMock.Setup(r => r.RemoveAsync(_chatId, _messageId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(result);
    }

    [Fact]
    public async Task Set_StoresTheReaction_AndTellsAllMembers()
    {
        Changes(found: true, changed: true);

        var result = await Service().SetAsync(_chatId, _userId, _messageId, "👍");

        Assert.Equal("👍", result.Emoji);
        Assert.Equal(_userId, result.UserId);
        Assert.Equal(1, Assert.Single(result.Reactions).Count);
        _eventsMock.Verify(e => e.ReactionsChangedAsync(result,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_TheSameAgain_SendsNothing()
    {
        Changes(found: true, changed: false);

        await Service().SetAsync(_chatId, _userId, _messageId, "👍");

        _eventsMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("🦄")]
    [InlineData("like")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Set_ReactionOutsideTheSet_IsRejectedBeforeTheDatabase(string? emoji)
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() => Service().SetAsync(_chatId, _userId, _messageId, emoji));

        Assert.Equal(ReactionService.InvalidReactionCode, ex.ErrorCode);
        _reactionsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Set_HeartWithoutTheVariationSelector_IsTheConfiguredHeart()
    {
        Changes(found: true, changed: true);

        await Service().SetAsync(_chatId, _userId, _messageId, "❤");

        _reactionsMock.Verify(r => r.SetAsync(_chatId, _messageId, _userId, "❤️", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_TheSetComesFromTheConfiguration()
    {
        Changes(found: true, changed: true);
        var service = Service(new MessageOptions { Reactions = ["🦄"] });

        await service.SetAsync(_chatId, _userId, _messageId, "🦄");
        await Assert.ThrowsAsync<BadRequestException>(() => service.SetAsync(_chatId, _userId, _messageId, "👍"));
    }

    [Fact]
    public async Task Set_ByNonMember_IsForbidden()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service().SetAsync(Guid.NewGuid(), _userId, _messageId, "👍"));

        Assert.Equal(ChatPolicy.NotAMemberCode, ex.ErrorCode);
        _reactionsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Set_OnAMissingMessage_IsNotFound()
    {
        Changes(found: false, changed: false);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Service().SetAsync(_chatId, _userId, _messageId, "👍"));

        Assert.Equal("MESSAGE_NOT_FOUND", ex.ErrorCode);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Remove_TellsMembersWithoutAnEmoji_AndIsQuietWhenThereWasNone()
    {
        Changes(found: true, changed: true, summary: null);
        await Service().RemoveAsync(_chatId, _userId, _messageId);
        _eventsMock.Verify(e => e.ReactionsChangedAsync(
            It.Is<MessageReactionsDto>(r => r.Emoji == null && r.Reactions.Count == 0),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);

        _eventsMock.Invocations.Clear();
        Changes(found: true, changed: false, summary: null);
        await Service().RemoveAsync(_chatId, _userId, _messageId);
        _eventsMock.VerifyNoOtherCalls();
    }

    /// <summary>The author of the message is a member; their chat-list row counts 1 new reaction.</summary>
    private void AuthorInChat()
    {
        _chatRepoMock.Setup(r => r.GetMemberIdsAsync(_chatId, It.IsAny<CancellationToken>())).ReturnsAsync([_userId, _authorId]);
        _chatRepoMock.Setup(r => r.GetChatListItemAsync(_chatId, _authorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatListResult { ChatId = _chatId, LastReadSeq = 4, UnreadReactionCount = 1 });
        _messagesMock.Setup(r => r.GetAsync(_chatId, _messageId, It.IsAny<CancellationToken>())).ReturnsAsync(new MessageWithSender
        {
            Id = _messageId, ChatId = _chatId, SenderId = _authorId, SenderName = "Алиса", Text = "Пойдём в кино?",
            Seq = 3, Type = "text"
        });
        _usersMock.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = _userId, DisplayName = "Боб" });
    }

    [Fact]
    public async Task Set_OnAnotherMembersMessage_UpdatesTheirCounters_AndNotifiesThem()
    {
        AuthorInChat();
        Changes(found: true, changed: true, author: _authorId);

        await Service().SetAsync(_chatId, _userId, _messageId, "👍");

        _eventsMock.Verify(e => e.ReadStateChangedAsync(
            It.Is<ReadStateDto>(s => s.ChatId == _chatId && s.UnreadReactionCount == 1 && s.LastReadSeq == 4),
            _authorId, It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.Verify(e => e.ReactionAddedAsync(
            It.Is<PushNotificationDto>(n => n.Kind == "reaction" && n.Emoji == "👍" && n.SenderId == _userId &&
                                            n.SenderName == "Боб" && n.MessageId == _messageId && n.Text == "Пойдём в кино?"),
            _authorId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_FromAnotherMembersMessage_UpdatesTheirCounters_WithoutANotification()
    {
        AuthorInChat();
        Changes(found: true, changed: true, summary: null, author: _authorId);

        await Service().RemoveAsync(_chatId, _userId, _messageId);

        _eventsMock.Verify(e => e.ReadStateChangedAsync(It.IsAny<ReadStateDto>(), _authorId, It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.Verify(e => e.ReactionAddedAsync(
            It.IsAny<PushNotificationDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Set_OnOwnMessage_IsNoNewsForAnyone()
    {
        Changes(found: true, changed: true, author: _userId);

        await Service().SetAsync(_chatId, _userId, _messageId, "👍");

        _eventsMock.Verify(e => e.ReactionsChangedAsync(
            It.IsAny<MessageReactionsDto>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.VerifyNoOtherCalls();
    }
}
