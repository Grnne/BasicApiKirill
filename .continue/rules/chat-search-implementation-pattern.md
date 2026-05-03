---
globs: "**/*.cs"
description: Apply when implementing chat search functionality similar to GET
  /api/chats/search
alwaysApply: false
---

When implementing chat search features (search by title for group chats, search by companion name/username for private chats), follow this pattern:
1. Add repository method(s) in IChatRepository / ChatRepository with ILIKE queries and LATERAL JOINs for companion data
2. Add DTO (SearchChatsResponseDto) in Models/Dto/Chat/
3. Add service method in IChatService / ChatService that branches by type parameter
4. Add handler method in ChatsHandler that delegates to service
5. Add endpoint in ChatsController at GET /api/chats/search?q=&type=&limit=
6. Add migration with GIN trigram indexes for ILIKE performance
7. Write handler tests (success, empty results, validation) and service tests (mapping, filtering, merging)