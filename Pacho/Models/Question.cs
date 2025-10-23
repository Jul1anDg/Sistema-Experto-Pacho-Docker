using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Pacho.Models
{
    public class Question
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Texto de la pregunta")]
        public string QuestionText { get; set; }

        [Display(Name = "Fecha de creación")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Display(Name = "Orden de aparición")]
        public int Order { get; set; }  // define la posición en el cuestionariox

        // Relación 1:N
        public ICollection<Answer> Answers { get; set; } = new List<Answer>();

    }
}