namespace BasicApi.Storage.Dto;

public sealed record UserUpdate(long Pts, string Type, string Payload, DateTime CreatedAt);
