using BasicApi.Hubs;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
using BasicApi.Tests.TestDoubles;

namespace BasicApi.Tests.Services.Events;

/// <summary>The event format on the wire is a contract with the frontend: names, recipients and arguments.</summary>
public class SignalRChatEventPublisherTests
{
    private readonly RecordingHubContext _hub = new();
    private readonly SignalRChatEventPublisher _publisher;

    public SignalRChatEventPublisherTests() => _publisher = new SignalRChatEventPublisher(_hub, new HubConnectionRegistry());

    private static MessageDto Message(string text) => new()
    {
        Id = Guid.NewGuid(),
        ChatId = Guid.NewGuid(),
        SenderId = Guid.NewGuid(),
        SenderName = "Alice",
        Text = text,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task MessageCreated_GoesToChatGroup_WithFullMessage()
    {
        var message = Message("hello");

        await _publisher.MessageCreatedAsync(message, [message.SenderId]);

        var sent = Assert.Single(_hub.Recorder.Of("MessageCreated"));
        Assert.Equal("group", sent.Target);
        Assert.Equal([message.ChatId.ToString()], sent.Ids);
        Assert.Same(message, Assert.Single(sent.Args));
    }

    [Fact]
    public async Task MessageCreated_UpdatesChatListOfAllMembers_InOneCall_WithTruncatedPreview()
    {
        var message = Message(new string('x', 150));
        Guid[] members = [message.SenderId, Guid.NewGuid()];

        await _publisher.MessageCreatedAsync(message, members);

        var sent = Assert.Single(_hub.Recorder.Of("ChatListUpdated"));
        Assert.Equal("users", sent.Target);
        Assert.Equal(members.Select(m => m.ToString()), sent.Ids);
        Assert.Equal(message.ChatId, sent.Args[0]);
        var preview = Assert.IsType<MessageDto>(sent.Args[1]);
        Assert.Equal(new string('x', 100) + "…", preview.Text);
        Assert.Equal(message.Id, preview.Id);
    }

    [Fact]
    public async Task MessageCreated_PreviewCarriesWhatTheChatRowShows()
    {
        // The contract promised seq, and the client could not place a preview without it: it
        // dropped every one, and the rows of chats not open waited for the catch-up.
        var message = Message("");
        message.Seq = 42;
        message.Type = "system";
        message.Action = new MessageActionDto { Type = "title_changed" };
        message.Attachments = [new AttachmentDto { Id = Guid.NewGuid(), Kind = "photo" }];

        await _publisher.MessageCreatedAsync(message, [message.SenderId]);

        var preview = Assert.IsType<MessageDto>(Assert.Single(_hub.Recorder.Of("ChatListUpdated")).Args[1]);
        Assert.Equal(42, preview.Seq);
        Assert.Equal("system", preview.Type);
        Assert.Equal("title_changed", preview.Action?.Type);
        Assert.Single(preview.Attachments);
    }

    [Fact]
    public async Task MessageCreated_PreviewCoversSpoilers_ItHasNoFormattingToHideThem()
    {
        var message = Message("the killer is the butler");
        message.Entities = [new MessageEntityDto { Type = "spoiler", Offset = 14, Length = 10 }];

        await _publisher.MessageCreatedAsync(message, [message.SenderId]);

        var preview = Assert.IsType<MessageDto>(Assert.Single(_hub.Recorder.Of("ChatListUpdated")).Args[1]);
        Assert.Equal("the killer is ▒▒▒▒", preview.Text);
    }

    [Fact]
    public async Task MessageCreated_ShortText_PreviewIsTheSame()
    {
        var message = Message("short");

        await _publisher.MessageCreatedAsync(message, [message.SenderId]);

        var preview = Assert.IsType<MessageDto>(Assert.Single(_hub.Recorder.Of("ChatListUpdated")).Args[1]);
        Assert.Equal("short", preview.Text);
    }

    [Fact]
    public async Task ChatCreated_GoesToRecipientOnly()
    {
        var recipient = Guid.NewGuid();
        var item = new ChatListItemDto { ChatId = Guid.NewGuid(), Type = "private" };

        await _publisher.ChatCreatedAsync(recipient, item);

        var sent = Assert.Single(_hub.Recorder.Sent);
        Assert.Equal(("user", "ChatCreated"), (sent.Target, sent.Method));
        Assert.Equal([recipient.ToString()], sent.Ids);
        Assert.Same(item, Assert.Single(sent.Args));
    }

    [Fact]
    public async Task UserOnlineChanged_GoesToRecipients_InOneCall()
    {
        var userId = Guid.NewGuid();
        Guid[] recipients = [Guid.NewGuid(), Guid.NewGuid()];

        await _publisher.UserOnlineChangedAsync(userId, true, recipients);

        var sent = Assert.Single(_hub.Recorder.Sent);
        Assert.Equal(("users", "UserOnlineChanged"), (sent.Target, sent.Method));
        Assert.Equal(recipients.Select(r => r.ToString()), sent.Ids);
        Assert.Equal([userId, true], sent.Args);
    }

    [Fact]
    public async Task TypingChanged_GoesToRecipients_InOneCall()
    {
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        await _publisher.TypingChangedAsync(chatId, userId, false, [recipient]);

        var sent = Assert.Single(_hub.Recorder.Sent);
        Assert.Equal(("users", "TypingChanged"), (sent.Target, sent.Method));
        Assert.Equal([chatId, userId, false], sent.Args);
    }

    [Fact]
    public async Task NoRecipients_NothingIsSent()
    {
        await _publisher.UserOnlineChangedAsync(Guid.NewGuid(), true, []);
        await _publisher.TypingChangedAsync(Guid.NewGuid(), Guid.NewGuid(), true, []);

        Assert.Empty(_hub.Recorder.Sent);
    }
}
