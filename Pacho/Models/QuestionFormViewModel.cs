using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Pacho.Models
{
    public class QuestionFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El texto de la pregunta es obligatorio.")]
        public string QuestionText { get; set; } = "";

        [Required(ErrorMessage = "El orden de la pregunta es obligatorio.")]
        public int Order { get; set; }

        public List<AnswerItemVM> Answers { get; set; } = new();
    }

    public class AnswerItemVM
    {
        public int Id { get; set; } // 0 = nueva

        [Required(ErrorMessage = "El texto de la respuesta es obligatorio.")]
        public string AnswerText { get; set; } = "";

        public bool IsCorrect { get; set; }
    }
}
