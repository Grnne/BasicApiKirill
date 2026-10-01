using BasicApi.Models.Dto.Message;

namespace BasicApi.Features.Messages;

/// <summary>A piece of history that may sit in the middle: older pages by cursor, newer by seq.</summary>
public class MessageWindowDto
{
    /// <summary>Oldest first.</summary>
    public List<MessageDto> Items { get; set; } = [];

    /// <summary>Pass as <c>cursor</c> to <c>GET …/messages/cursor</c> for the page before the first item.</summary>
    public string? NextCursor { get; set; }

    /// <summary>There are older messages before the first item.</summary>
    public bool HasMore { get; set; }

    /// <summary>There are newer messages after the last item: <c>GET …/messages/after?seq=</c> its seq.</summary>
    public bool HasNewer { get; set; }
}
