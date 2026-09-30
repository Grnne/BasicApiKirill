using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.TestDoubles;

public static class ChatRepositoryMockExtensions
{
    /// <summary>
    /// <c>GetMemberAsync</c> follows the <c>IsMemberAsync</c> setups: a member there is a member of a
    /// private chat here. Tests about roles set up <c>GetMemberAsync</c> themselves.
    /// </summary>
    public static Mock<IChatRepository> WithMembersFromIsMember(this Mock<IChatRepository> mock)
    {
        mock.Setup(r => r.GetMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid chatId, Guid userId, CancellationToken ct) =>
                await mock.Object.IsMemberAsync(chatId, userId, ct)
                    ? new ChatMember { ChatId = chatId, UserId = userId, ChatType = ChatTypes.Private, Role = ChatRoles.Member }
                    : null);
        return mock;
    }
}
