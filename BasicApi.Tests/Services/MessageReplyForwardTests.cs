using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using BasicApi.Tests.TestDoubles;
using Moq;

namespace BasicApi.Tests.Services;

/// <summary>Replies and forwards (plan 2, F1.2).</summary>
public class MessageReplyForwardTests
{
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IMessageRepository> _msgRepoMock = new();
    private readonly Mock<IChatEventPublisher> _eventsMock = new();
    private readonly MessageService _service;
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly Guid _sourceChatId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _author = Guid.NewGuid();
    private readonly List<Message> _created = [];

    public MessageReplyForwardTests()
    {
        foreach (var chat in new[] { _chatId, _sourceChatId })
        {
            _chatRepoMock.Setup(r => r.IsMemberAsync(chat, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _chatRepoMock.Setup(r => r.GetMemberIdsAsync(chat, It.IsAny<CancellationToken>())).ReturnsAsync([_userId, _author]);
        }

        _msgRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Message>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Message m, Guid? clientMessageId, CancellationToken _) =>
            {
                _created.Add(m);
                return new MessageWithSender
                {
                    Id = m.Id, ChatId = m.ChatId, SenderId = m.SenderId, Text = m.Text, CreatedAt = m.CreatedAt,
                    Seq = _created.Count, ClientMessageId = clientMessageId, SenderName = "Me",
                    ReplyToMessageId = m.ReplyToMessageId,
                    ForwardFromUserId = m.ForwardFromUserId, ForwardFromChatId = m.ForwardFromChatId,
                    ForwardFromMessageId = m.ForwardFromMessageId, ForwardFromUserName = "Original"
                };
            });

        var membership = new MembershipService(_chatRepoMock.Object);
        _service = new MessageService(new FakeDbSession(), _msgRepoMock.Object, membership, new ChatPolicy(membership), _eventsMock.Object, Mock.Of<IDraftRepository>());
    }

    private MessageWithSender Source(string text, long seq, Guid? forwardFromUser = null) => new()
    {
        Id = Guid.NewGuid(), ChatId = _sourceChatId, SenderId = _author, SenderName = "Author", Text = text, Seq = seq,
        CreatedAt = DateTime.UtcNow,
        ForwardFromUserId = forwardFromUser,
        ForwardFromChatId = forwardFromUser is null ? null : Guid.NewGuid(),
        ForwardFromMessageId = forwardFromUser is null ? null : Guid.NewGuid()
    };

    private void Visible(params MessageWithSender[] messages) =>
        _msgRepoMock
            .Setup(r => r.GetVisibleAsync(_sourceChatId, _userId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);

    // ========== Reply ==========

    [Fact]
    public async Task Reply_ToAMessageOfTheChat_IsStoredWithTheLink()
    {
        var target = Guid.NewGuid();
        _msgRepoMock.Setup(r => r.GetAsync(_chatId, target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageWithSender { Id = target, ChatId = _chatId });

        var result = await _service.SendAsync(_chatId, _userId, "yes", replyToMessageId: target);

        Assert.Equal(target, Assert.Single(_created).ReplyToMessageId);
        Assert.Equal(target, result.Message.ReplyTo?.MessageId);
    }

    [Fact]
    public async Task Reply_ToAMessageOutsideTheChat_IsRejected_NothingStored()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.SendAsync(_chatId, _userId, "yes", replyToMessageId: Guid.NewGuid()));

        Assert.Equal("REPLY_TARGET_NOT_FOUND", ex.ErrorCode);
        Assert.Empty(_created);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reply_ToADeletedMessage_IsRejected()
    {
        var target = Guid.NewGuid();
        _msgRepoMock.Setup(r => r.GetAsync(_chatId, target, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageWithSender { Id = target, ChatId = _chatId, DeletedAt = DateTime.UtcNow });

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.SendAsync(_chatId, _userId, "yes", replyToMessageId: target));

        Assert.Equal("REPLY_TARGET_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task ReplyPreview_IsTruncated_AndEmptyWhenTheTargetIsDeleted()
    {
        var target = Guid.NewGuid();
        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(_chatId, _userId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorResult<MessageWithSender>
            {
                Items =
                [
                    new() { Id = Guid.NewGuid(), ChatId = _chatId, Seq = 2, ReplyToMessageId = target, ReplyToSenderName = "Bob", ReplyToText = new string('a', 150) },
                    new() { Id = Guid.NewGuid(), ChatId = _chatId, Seq = 1, ReplyToMessageId = target, ReplyToText = "", ReplyToDeleted = true }
                ]
            });

        var page = await _service.GetPageAsync(_chatId, _userId, null, 20);

        var deleted = page.Items[0].ReplyTo!;
        var live = page.Items[1].ReplyTo!;
        Assert.True(deleted.Deleted);
        Assert.Equal("", deleted.Text);
        Assert.Equal(SignalRChatEventPublisher.PreviewLength + 1, live.Text.Length); // with the ellipsis
        Assert.Equal("Bob", live.SenderName);
        Assert.Null(page.Items[0].ForwardFrom);
    }

    // ========== Forward ==========

    [Fact]
    public async Task Forward_CopiesMessagesInOrder_WithTheOriginalAuthor_AndAnnouncesEach()
    {
        var first = Source("first", 1);
        var second = Source("second", 2);
        Visible(first, second);

        var result = await _service.ForwardAsync(_chatId, _userId, _sourceChatId, [second.Id, first.Id]);

        Assert.True(result.Created);
        Assert.Equal(["first", "second"], result.Messages.Select(m => m.Text));
        Assert.All(_created, m =>
        {
            Assert.Equal(_chatId, m.ChatId);
            Assert.Equal(_userId, m.SenderId);
            Assert.Equal(_author, m.ForwardFromUserId);
            Assert.Equal(_sourceChatId, m.ForwardFromChatId);
            Assert.Null(m.ReplyToMessageId);
        });
        Assert.Equal([first.Id, second.Id], _created.Select(m => m.ForwardFromMessageId!.Value));
        Assert.Equal("Original", result.Messages[0].ForwardFrom?.SenderName);
        _eventsMock.Verify(e => e.MessageCreatedAsync(It.IsAny<MessageDto>(), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Forward_OfAForward_KeepsTheFirstOriginal()
    {
        var original = Guid.NewGuid();
        var forwarded = Source("chain", 1, forwardFromUser: original);
        Visible(forwarded);

        await _service.ForwardAsync(_chatId, _userId, _sourceChatId, [forwarded.Id]);

        var copy = Assert.Single(_created);
        Assert.Equal(original, copy.ForwardFromUserId);
        Assert.Equal(forwarded.ForwardFromChatId, copy.ForwardFromChatId);
        Assert.Equal(forwarded.ForwardFromMessageId, copy.ForwardFromMessageId);
    }

    [Fact]
    public async Task Forward_MessageTheUserDoesNotSee_Is404_NothingCopied()
    {
        var visible = Source("visible", 1);
        Visible(visible);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.ForwardAsync(_chatId, _userId, _sourceChatId, [visible.Id, Guid.NewGuid()]));

        Assert.Equal("MESSAGE_NOT_FOUND", ex.ErrorCode);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task Forward_FromAChatTheUserIsNotIn_IsForbidden()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.ForwardAsync(_chatId, _userId, Guid.NewGuid(), [Guid.NewGuid()]));

        Assert.Equal(ChatPolicy.NotAMemberCode, ex.ErrorCode);
        _msgRepoMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Forward_BadRequests_HaveCodes()
    {
        var id = Guid.NewGuid();

        Assert.Equal("INVALID_REQUEST", (await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.ForwardAsync(_chatId, _userId, _sourceChatId, []))).ErrorCode);
        Assert.Equal("INVALID_REQUEST", (await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.ForwardAsync(_chatId, _userId, _sourceChatId, [id, id]))).ErrorCode);
        Assert.Equal("INVALID_REQUEST", (await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.ForwardAsync(_chatId, _userId, _sourceChatId, [id], [Guid.NewGuid(), Guid.NewGuid()]))).ErrorCode);
        Assert.Equal("TOO_MANY_MESSAGES", (await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.ForwardAsync(_chatId, _userId, _sourceChatId,
                [.. Enumerable.Range(0, MessageService.MaxForward + 1).Select(_ => Guid.NewGuid())]))).ErrorCode);
        _msgRepoMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Forward_Retry_ReturnsTheEarlierCopies_WithoutNewOnesOrEvents()
    {
        var source = Source("once", 1);
        Visible(source);
        var clientId = Guid.NewGuid();
        var earlier = new MessageWithSender { Id = Guid.NewGuid(), ChatId = _chatId, SenderId = _userId, Text = "once", ClientMessageId = clientId };
        _msgRepoMock.Setup(r => r.GetByClientMessageIdAsync(_userId, clientId, It.IsAny<CancellationToken>())).ReturnsAsync(earlier);

        var result = await _service.ForwardAsync(_chatId, _userId, _sourceChatId, [source.Id], [clientId]);

        Assert.False(result.Created);
        Assert.Equal(earlier.Id, Assert.Single(result.Messages).Id);
        Assert.Empty(_created);
        _eventsMock.VerifyNoOtherCalls();
    }

    // ========== Editing a forward ==========

    [Fact]
    public async Task Edit_OfAForwardedMessage_IsNotAllowed()
    {
        var id = Guid.NewGuid();
        _msgRepoMock.Setup(r => r.GetAsync(_chatId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageWithSender
            {
                Id = id, ChatId = _chatId, SenderId = _userId, Text = "their words", CreatedAt = DateTime.UtcNow,
                ForwardFromUserId = _author
            });

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _service.EditAsync(_chatId, _userId, id, "my words"));

        Assert.Equal(ChatPolicy.MessageNotEditableCode, ex.ErrorCode);
    }
}
