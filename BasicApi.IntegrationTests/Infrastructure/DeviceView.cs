using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>
/// What a client knows about its user, kept the way a client keeps it: a snapshot
/// (<c>GET /api/sync/state</c> and the history) and then the updates of <c>GET /api/sync</c>,
/// applied in order. Two devices of one user must end up with equal views (plan 2, F8, D3).
/// </summary>
public sealed class DeviceView(Guid me)
{
    private sealed class Chat
    {
        public string Type = "";
        public string? Title;
        public Guid? CompanionId;
        public string? CompanionName;
        public Guid? AvatarId;
        public int? PinnedPosition;
        public bool Archived;
        public bool IsMuted;
        public DateTime? MutedUntil;
        public string? DraftText;
        public Guid? DraftReplyTo;
        public long LastReadSeq;
        public bool MarkedUnread;

        /// <summary>Own messages up to these seqs reached / were read by someone else.</summary>
        public long DeliveredSeq;
        public long ReadSeq;

        public readonly SortedDictionary<long, JsonObject> Messages = [];
    }

    private readonly Dictionary<Guid, Chat> _chats = [];
    private readonly HashSet<Guid> _blocked = [];
    private string _folders = "[]";
    private string _privacy = "";
    private string _myName = "";
    private Guid? _myAvatar;

    public long Pts { get; private set; }

    /// <summary>A fresh device: the snapshot, its chats' history page by page, and its pts.</summary>
    public static DeviceView FromSnapshot(Guid me, JsonElement state, IReadOnlyDictionary<Guid, List<JsonElement>> histories)
    {
        var view = new DeviceView(me) { Pts = state.GetProperty("pts").GetInt64() };
        foreach (var item in state.GetProperty("chats").EnumerateArray())
        {
            var chat = view.ChatOf(item.GetProperty("chatId").GetGuid());
            Card(chat, item);
            chat.ReadSeq = item.GetProperty("outboxReadSeq").GetInt64();
            chat.DeliveredSeq = item.GetProperty("outboxDeliveredSeq").GetInt64();
            foreach (var message in histories[item.GetProperty("chatId").GetGuid()])
                chat.Messages[message.GetProperty("seq").GetInt64()] = (JsonObject)JsonNode.Parse(message.GetRawText())!;
            // The counters of the snapshot agree with its history.
            var (unread, mentions) = Unread(me, chat);
            Assert.Equal((unread.Count, mentions), (item.GetProperty("unreadCount").GetInt32(), item.GetProperty("unreadMentionCount").GetInt32()));
            Assert.Equal(chat.Messages.Values.LastOrDefault()?["id"]?.GetValue<Guid>(), OptionalGuid(item.GetProperty("lastMessage") is
                { ValueKind: JsonValueKind.Object } lastMessage ? lastMessage : default, "id"));
        }
        view._folders = Canonical(state.GetProperty("folders"));
        view._privacy = Canonical(state.GetProperty("privacy"));
        foreach (var blocked in state.GetProperty("blockedUserIds").EnumerateArray())
            view._blocked.Add(blocked.GetGuid());
        var profile = state.GetProperty("me");
        view._myName = profile.GetProperty("displayName").GetString()!;
        view._myAvatar = OptionalGuid(profile, "avatarId");
        return view;
    }

    /// <summary>One update of <c>GET /api/sync</c>; they must come in pts order.</summary>
    public void Apply(JsonElement update)
    {
        var pts = update.GetProperty("pts").GetInt64();
        Assert.Equal(Pts + 1, pts);
        Pts = pts;
        var p = update.GetProperty("payload");
        var type = update.GetProperty("type").GetString();
        switch (type)
        {
            case "ChatCreated":
                // The card's last message is only a preview: messages come whole as MessageCreated.
                Card(ChatOf(p.GetProperty("chatId").GetGuid()), p);
                break;
            case "ChatUpdated":
                var updated = ChatOf(p.GetProperty("chatId").GetGuid());
                updated.Title = p.GetProperty("title").GetString();
                updated.AvatarId = OptionalGuid(p, "avatarId");
                break;
            case "ChatDeleted":
                _chats.Remove(p.GetProperty("chatId").GetGuid());
                break;
            case "MemberRemoved":
                if (p.GetProperty("userId").GetGuid() == me)
                    _chats.Remove(p.GetProperty("chatId").GetGuid());
                break;
            case "MemberAdded" or "MemberUpdated":
                break; // members are not part of the view
            case "MessageCreated" or "MessageUpdated":
                ChatOf(p.GetProperty("chatId").GetGuid()).Messages[p.GetProperty("seq").GetInt64()] =
                    (JsonObject)JsonNode.Parse(p.GetRawText())!;
                break;
            case "MessageDeleted":
                ChatOf(p.GetProperty("chatId").GetGuid()).Messages.Remove(p.GetProperty("seq").GetInt64());
                break;
            case "ReactionsChanged":
                var messageId = p.GetProperty("messageId").GetGuid();
                var target = ChatOf(p.GetProperty("chatId").GetGuid()).Messages.Values
                    .FirstOrDefault(m => m["id"]!.GetValue<Guid>() == messageId);
                if (target is null) break;
                target["reactions"] = JsonNode.Parse(p.GetProperty("reactions").GetRawText());
                if (p.GetProperty("userId").GetGuid() == me)
                    target["myReaction"] = p.GetProperty("emoji").GetString();
                break;
            case "MessagesDelivered" or "MessagesRead":
                if (p.GetProperty("userId").GetGuid() == me) break;
                var receipts = ChatOf(p.GetProperty("chatId").GetGuid());
                var seq = p.GetProperty("seq").GetInt64();
                receipts.DeliveredSeq = Math.Max(receipts.DeliveredSeq, seq);
                if (type == "MessagesRead")
                    receipts.ReadSeq = Math.Max(receipts.ReadSeq, seq);
                break;
            case "ReadStateChanged":
                var read = ChatOf(p.GetProperty("chatId").GetGuid());
                read.LastReadSeq = p.GetProperty("lastReadSeq").GetInt64();
                read.MarkedUnread = p.GetProperty("markedUnread").GetBoolean();
                break;
            case "DraftUpdated":
                Draft(ChatOf(p.GetProperty("chatId").GetGuid()), p.GetProperty("draft"));
                break;
            case "PinnedChatsChanged":
                var pinned = p.GetProperty("chatIds").EnumerateArray().Select(c => c.GetGuid()).ToList();
                foreach (var (id, chat) in _chats)
                    chat.PinnedPosition = pinned.IndexOf(id) is var i and >= 0 ? i + 1 : null;
                break;
            case "ChatStateChanged":
                var state = ChatOf(p.GetProperty("chatId").GetGuid());
                state.Archived = p.GetProperty("archived").GetBoolean();
                state.IsMuted = p.GetProperty("isMuted").GetBoolean();
                state.MutedUntil = OptionalTime(p, "mutedUntil");
                break;
            case "FoldersChanged":
                _folders = Canonical(p.GetProperty("folders"));
                break;
            case "PrivacyUpdated":
                _privacy = Canonical(p);
                break;
            case "BlockListChanged":
                if (p.GetProperty("blocked").GetBoolean()) _blocked.Add(p.GetProperty("userId").GetGuid());
                else _blocked.Remove(p.GetProperty("userId").GetGuid());
                break;
            case "UserUpdated":
                var userId = p.GetProperty("userId").GetGuid();
                var name = p.GetProperty("displayName").GetString()!;
                if (userId == me)
                {
                    _myName = name;
                    _myAvatar = OptionalGuid(p, "avatarId");
                }
                foreach (var chat in _chats.Values)
                {
                    if (chat.CompanionId == userId)
                    {
                        chat.CompanionName = name;
                        chat.AvatarId = OptionalGuid(p, "avatarId");
                    }
                    foreach (var m in chat.Messages.Values.Where(m => m["senderId"]!.GetValue<Guid>() == userId))
                        m["senderName"] = name;
                }
                break;
            default:
                Assert.Fail($"The device does not know the update {type}");
                break;
        }
    }

    /// <summary>Everything the view holds, one fact per line, in a stable order.</summary>
    public List<string> Describe()
    {
        var lines = new List<string>
        {
            $"me name={_myName} avatar={_myAvatar}",
            $"privacy {_privacy}",
            $"folders {_folders}",
            $"blocked {string.Join(",", _blocked.Order())}"
        };
        foreach (var (id, c) in _chats.OrderBy(c => c.Key))
        {
            var visible = c.Messages.Values.ToList();
            var (unread, mentions) = Unread(me, c);
            lines.Add($"chat {id} type={c.Type} title={c.Title} companion={c.CompanionId}/{c.CompanionName} avatar={c.AvatarId} " +
                      $"pinned={c.PinnedPosition} archived={c.Archived} muted={c.IsMuted}/{Ms(c.MutedUntil)} " +
                      $"draft={c.DraftText}/{c.DraftReplyTo} read={c.LastReadSeq} markedUnread={c.MarkedUnread} " +
                      $"unread={unread.Count} mentions={mentions} last={visible.LastOrDefault()?["id"]}");
            foreach (var m in visible)
            {
                var seq = m["seq"]!.GetValue<long>();
                var status = Sender(m) != me || c.Type == "saved" ? null
                    : seq <= c.ReadSeq ? "read" : seq <= c.DeliveredSeq ? "delivered" : "sent";
                lines.Add($"  msg {seq} {Project(m)} status={status}");
            }
        }
        return lines;
    }

    private Chat ChatOf(Guid chatId)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
            _chats[chatId] = chat = new Chat();
        return chat;
    }

    private static void Card(Chat chat, JsonElement item)
    {
        chat.Type = item.GetProperty("type").GetString()!;
        chat.Title = item.GetProperty("title").GetString();
        chat.CompanionId = OptionalGuid(item, "companionId");
        chat.CompanionName = item.GetProperty("companionName").GetString();
        chat.AvatarId = OptionalGuid(item, "avatarId");
        chat.PinnedPosition = item.GetProperty("pinnedPosition") is { ValueKind: JsonValueKind.Number } pin ? pin.GetInt32() : null;
        chat.Archived = item.GetProperty("archived").GetBoolean();
        chat.IsMuted = item.GetProperty("isMuted").GetBoolean();
        chat.MutedUntil = OptionalTime(item, "mutedUntil");
        chat.LastReadSeq = item.GetProperty("lastReadSeq").GetInt64();
        chat.MarkedUnread = item.GetProperty("markedUnread").GetBoolean();
        Draft(chat, item.GetProperty("draft"));
    }

    private static void Draft(Chat chat, JsonElement draft)
    {
        chat.DraftText = draft.ValueKind == JsonValueKind.Null ? null : draft.GetProperty("text").GetString();
        chat.DraftReplyTo = draft.ValueKind == JsonValueKind.Null ? null : OptionalGuid(draft, "replyToMessageId");
    }

    private static Guid Sender(JsonObject m) => m["senderId"]!.GetValue<Guid>();

    /// <summary>The server's rule: others' messages after the read pointer; mentions of the user among them.</summary>
    private static (List<JsonObject> Unread, int Mentions) Unread(Guid me, Chat c)
    {
        var unread = c.Messages.Values.Where(m => m["seq"]!.GetValue<long>() > c.LastReadSeq && Sender(m) != me).ToList();
        return (unread, unread.Count(m => m["entities"]!.AsArray()
            .Any(e => e!["type"]!.GetValue<string>() == "mention" && e["userId"]?.GetValue<Guid>() == me)));
    }

    /// <summary>What a message shows; times to the millisecond (events carry more digits than the database).</summary>
    private static string Project(JsonObject m) => Sorted(new JsonObject
    {
        ["id"] = m["id"]!.DeepClone(),
        ["sender"] = $"{m["senderId"]}/{m["senderName"]}",
        ["type"] = m["type"]!.DeepClone(),
        ["text"] = m["text"]!.DeepClone(),
        ["createdAt"] = Ms(m["createdAt"]!.GetValue<DateTime>()),
        ["editedAt"] = Ms(m["editedAt"]?.GetValue<DateTime?>()),
        ["entities"] = m["entities"]!.DeepClone(),
        ["replyTo"] = m["replyTo"]?["messageId"]?.DeepClone(),
        ["forwardFrom"] = m["forwardFrom"]?["senderId"]?.DeepClone(),
        ["attachments"] = new JsonArray([.. m["attachments"]!.AsArray().Select(a => (JsonNode?)$"{a!["id"]}/{a["state"]}")]),
        ["action"] = m["action"]?.DeepClone(),
        ["reactions"] = m["reactions"]!.DeepClone(),
        ["myReaction"] = m["myReaction"]?.DeepClone()
    })!.ToJsonString();

    private static string? Ms(DateTime? time) =>
        time?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static Guid? OptionalGuid(JsonElement e, string property) =>
        e.ValueKind == JsonValueKind.Object && e.GetProperty(property) is { ValueKind: JsonValueKind.String } v ? v.GetGuid() : null;

    private static DateTime? OptionalTime(JsonElement e, string property) =>
        e.GetProperty(property) is { ValueKind: JsonValueKind.String } v ? v.GetDateTime() : null;

    /// <summary>JSON with object keys sorted: the same object may come with its fields in another order.</summary>
    private static string Canonical(JsonElement e) => Sorted(JsonNode.Parse(e.GetRawText()))?.ToJsonString() ?? "null";

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value?.DeepClone())))),
        JsonArray a => new JsonArray([.. a.Select(i => Sorted(i?.DeepClone()))]),
        _ => node?.DeepClone()
    };

    /// <summary>Asserts the two devices see the same, showing the lines that differ in full.</summary>
    public static void AssertSame(DeviceView expected, DeviceView actual)
    {
        var a = expected.Describe();
        var b = actual.Describe();
        var missing = a.Except(b).ToList();
        var extra = b.Except(a).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            "The devices differ." + Environment.NewLine +
            "Only on the first:" + Environment.NewLine + string.Join(Environment.NewLine, missing) + Environment.NewLine +
            "Only on the second:" + Environment.NewLine + string.Join(Environment.NewLine, extra));
        Assert.Equal(a, b);
    }
}
