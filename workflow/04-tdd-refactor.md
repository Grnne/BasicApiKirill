# REFACTOR — Improve Code Without Changing Behavior

## Rules

1. **Tests are frozen** — if you need to change a test, behavior changed → new RED phase
2. **One refactor at a time** → `dotnet test` → green? Next.
3. **Run tests after every change**

## Priority Table

| Priority | Symptom | Action |
|----------|---------|--------|
| 🔴 High | Handler > 500 lines | Split into smaller methods / extract Service |
| 🔴 High | Service > 500 lines | Split into multiple services |
| 🔴 High | Test class > 500 lines | Split into multiple test classes |
| 🟡 Medium | Duplicate SQL in repos | Extract shared SQL constants or helper methods |
| 🟡 Medium | Magic strings/numbers | Extract to constants |
| 🟡 Medium | Mixed responsibilities | Split into two classes |
| 🟢 Low | Small methods, no duplication | Leave as-is |
| 🟢 Low | DTO == Entity shape | Leave as-is (DTOs can grow independently) |

## Typical Refactors for BasicApi

### Extract Service from Handler

**Before:**
```csharp
public class ChatsHandler(IChatRepository chatRepo, IMessageRepository msgRepo)
{
    public async Task<IActionResult> GetUserChatsAsync(Guid userId)
    {
        // 30+ lines of business logic mixed with IActionResult creation
    }
}
```

**After:**
```csharp
public class ChatsHandler(IChatService chatService)
{
    public async Task<IActionResult> GetUserChatsAsync(Guid userId)
    {
        var chats = await chatService.GetUserChatsAsync(userId);
        return new OkObjectResult(chats);
    }
}
```

### Split Large Migration

**Before:** One `InitialCreate.cs` with all tables.

**After:**
```
Migrations/
├── 001_InitialCreate.cs
├── 002_AddMessagesTable.cs
└── 003_AddIndexes.cs
```

## Check before commit

- [ ] All tests pass after all changes?
- [ ] Handler ≤ 500 lines?
- [ ] Service ≤ 500 lines?
- [ ] Test files ≤ 500 lines?
- [ ] No duplicate code?
- [ ] Names reflect responsibility?
