using System.ComponentModel.DataAnnotations;

namespace BasicApi.Models.Dto.Message;

public class MarkMessageReadDto
{
    [Required(ErrorMessage = "LastMessageId is required")]
    public Guid LastMessageId { get; set; }
}