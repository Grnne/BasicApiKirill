namespace BasicApi.Storage.Dto;

public sealed record OutboxRow(long Id, string Type, string Payload, int Attempts);
