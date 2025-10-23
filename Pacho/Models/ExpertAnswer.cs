using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Pacho.Models
{
    public class ExpertAnswer
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey("Expert")]
        public int ExpertId { get; set; }

        [Required]
        [ForeignKey("Question")]
        public int QuestionId { get; set; }

        [Required]
        [ForeignKey("Answer")]
        public int AnswerId { get; set; }

        public DateTime AnsweredAt { get; set; } = DateTime.Now;

        // Navegación
        public Expert Expert { get; set; }
        public Question Question { get; set; }
        public Answer Answer { get; set; }
    }
}
