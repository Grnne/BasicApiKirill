using System.ComponentModel.DataAnnotations;

namespace BasicApi.Features.Messages;

public class MarkMessageReadDto
{
    [Required(ErrorMessage = "LastMessageId is required")]
    public Guid LastMessageId { get; set; }
}