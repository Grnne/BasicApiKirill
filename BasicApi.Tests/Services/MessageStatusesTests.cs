using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Dto;

namespace BasicApi.Tests.Services;

/// <summary>Message status for the viewer.</summary>
public class MessageStatusesTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static readonly ReadPointers Pointers = new()
    {
        ReadSeq = 4, OutboxReadSeq = 5, OutboxDeliveredSeq = 7, HasOthers = true
    };

    [Theory]
    [InlineData(5, "read", true)]
    [InlineData(6, "delivered", false)]
    [InlineData(7, "delivered", false)]
    [InlineData(8, "sent", false)]
    public void OwnMessage_TakesTheFurthestOtherMember(long seq, string status, bool isRead)
    {
        var message = new MessageDto { SenderId = Me, Seq = seq };

        MessageStatuses.Apply(message, Me, Pointers);

        Assert.Equal(status, message.Status);
        Assert.Equal(isRead, message.IsRead);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void SomeoneElsesMessage_HasNoStatus_AndIsReadByTheViewersPointer(long seq, bool isRead)
    {
        var message = new MessageDto { SenderId = Other, Seq = seq };

        MessageStatuses.Apply(message, Me, Pointers);

        Assert.Null(message.Status);
        Assert.Equal(isRead, message.IsRead);
    }

    [Fact]
    public void InAChatWithOneself_OwnMessagesHaveNoStatus()
    {
        var message = new MessageDto { SenderId = Me, Seq = 1 };

        MessageStatuses.Apply(message, Me, new ReadPointers { ReadSeq = 1, HasOthers = false });

        Assert.Null(message.Status);
        Assert.False(message.IsRead);
    }
}
