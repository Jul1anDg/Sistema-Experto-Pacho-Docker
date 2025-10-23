using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pacho.Models
{
    public class DiagnosticAnswer
    {
        [Key]
        public int Id { get; set; }                 // -> id_answer

        [ForeignKey(nameof(Question))]
        public int QuestionId { get; set; }         // -> question_id

        public int AnswerOrder { get; set; }        // -> answer_order (1 o 2)

        [Required, MaxLength(200)]
        public string AnswerText { get; set; } = ""; // -> answer_text ('Yes' o 'No')

        public DiagnosticQuestion? Question { get; set; }
    }
}


