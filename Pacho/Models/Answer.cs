using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pacho.Models
{
    public class Answer
    {
        // Identificador único de la respuesta
        [Key]
        public int Id { get; set; }

        // Texto que contiene el contenido de la respuesta
        [Required]
        [Display(Name = "Texto de la respuesta")]
        public string AnswerText { get; set; }

        // Indica si esta respuesta es la correcta dentro de la pregunta asociada
        [Display(Name = "¿Es una respuesta correcta?")]
        public bool IsCorrect { get; set; }

        // Clave foránea que vincula la respuesta con su pregunta
        [ForeignKey("Question")]
        public int QuestionId { get; set; }

        // Determina si la respuesta está activa o visible en el sistema
        public bool IsActive { get; set; } = true;

        // Relación de navegación hacia la pregunta a la que pertenece
        public Question Question { get; set; }

        // Relación con las respuestas seleccionadas por expertos en los tests
        public ICollection<ExpertAnswer> ExpertAnswers { get; set; } = new List<ExpertAnswer>();
    }
}
