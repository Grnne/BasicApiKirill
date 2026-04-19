using BasicApi.Extensions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Chats;

[Authorize]
[ApiController]
[Route("api/chats")]
[Produces("application/json")]
[Tags("Chats")]
public class ChatsController(ChatsHandler handlers) : ControllerBase
{
    /// <summary>
    /// Получить список всех чатов текущего пользователя
    /// </summary>
    /// <returns>Список чатов с последними сообщениями</returns>
    /// <response code="200">Возвращает список чатов пользователя</response>
    /// <response code="401">Пользователь не авторизован</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUserChats()
        => await handlers.GetUserChatsAsync(User.GetUserId());

    /// <summary>
    /// Создать приватный чат с другим пользователем
    /// </summary>
    /// <remarks>
    /// Если чат уже существует, возвращает существующий чат.
    /// 
    /// Пример запроса:
    /// 
    ///     POST /api/chats/private/3fa85f64-5717-4562-b3fc-2c963f66afa6
    ///     
    /// </remarks>
    /// <param name="userId">ID пользователя, с которым создать чат</param>
    /// <returns>ID созданного или существующего чата</returns>
    /// <response code="200">Чат уже существует</response>
    /// <response code="201">Чат успешно создан</response>
    /// <response code="400">Нельзя создать чат с самим собой</response>
    /// <response code="401">Пользователь не авторизован</response>
    [HttpPost("private/{userId}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreatePrivateChat(Guid userId)
        => await handlers.CreatePrivateChatAsync(User.GetUserId(), userId);

    /// <summary>
    /// Получить детальную информацию о чате
    /// </summary>
    /// <param name="chatId">ID чата</param>
    /// <returns>Информация о чате и участниках</returns>
    /// <response code="200">Возвращает информацию о чате</response>
    /// <response code="401">Пользователь не авторизован</response>
    /// <response code="403">Пользователь не является участником чата</response>
    /// <response code="404">Чат не найден</response>
    [HttpGet("{chatId}")]
    [ProducesResponseType(typeof(ChatDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChat(Guid chatId)
        => await handlers.GetChatAsync(chatId, User.GetUserId());

    /// <summary>
    /// Получить историю сообщений в чате
    /// </summary>
    /// <param name="chatId">ID чата</param>
    /// <param name="before">Получить сообщения до указанной даты (опционально)</param>
    /// <param name="limit">Количество сообщений (по умолчанию 50, максимум 100)</param>
    /// <returns>Список сообщений в хронологическом порядке</returns>
    /// <response code="200">Возвращает список сообщений</response>
    /// <response code="401">Пользователь не авторизован</response>
    /// <response code="403">Пользователь не является участником чата</response>
    [HttpGet("{chatId}/messages")]
    [ProducesResponseType(typeof(IEnumerable<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessages(Guid chatId, [FromQuery] DateTime? before, [FromQuery] int limit = 50)
        => await handlers.GetMessagesAsync(chatId, User.GetUserId(), before, limit);

    /// <summary>
    /// Отметить сообщения как прочитанные
    /// </summary>
    /// <remarks>
    /// Пример запроса:
    /// 
    ///     POST /api/chats/3fa85f64-5717-4562-b3fc-2c963f66afa6/read
    ///     {
    ///         "lastMessageId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
    ///     }
    ///     
    /// </remarks>
    /// <param name="chatId">ID чата</param>
    /// <param name="dto">ID последнего прочитанного сообщения</param>
    /// <response code="200">Сообщения успешно отмечены как прочитанные</response>
    /// <response code="401">Пользователь не авторизован</response>
    /// <response code="403">Пользователь не является участником чата</response>
    [HttpPost("{chatId}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarkRead(Guid chatId, [FromBody] MarkMessageReadDto dto)
        => await handlers.MarkReadAsync(chatId, User.GetUserId(), dto.LastMessageId);
}