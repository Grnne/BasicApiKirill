namespace BasicApi.Storage.Dto;

/// <summary>A file put into a message: which one and of what kind.</summary>
public sealed record AttachmentRef(Guid Id, string Kind);
