// Storage/Dto/ChatParticipantRecord.cs
namespace BasicApi.Storage.Dto;

public record ChatParticipantDto(
    Guid UserId,
    string DisplayName,
    string Username
);