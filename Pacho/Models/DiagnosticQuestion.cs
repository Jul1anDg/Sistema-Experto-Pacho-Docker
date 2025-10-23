using System.ComponentModel.DataAnnotations;

namespace Pacho.Models
{
    public class DiagnosticQuestion
    {
        [Key]
        public int Id { get; set; }                       // -> id_question

        [Required, MaxLength(255)]
        public string QuestionText { get; set; } = "";    // -> question_text

        public int QuestionOrder { get; set; }            // -> question_order

        public DateTime? CreatedAt { get; set; }  // -> created_at

        public ICollection<DiagnosticAnswer> Answers { get; set; } = new List<DiagnosticAnswer>();
    }
}


