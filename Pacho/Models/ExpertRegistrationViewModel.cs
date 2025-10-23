using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Pacho.Models
{
    public class ExpertRegistrationViewModel
    {
        public string Name { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string ExperienceType { get; set; }
        public decimal ExperienceYears { get; set; } 
            
        [Required, Phone]
        [Display(Name = "Teléfono (móvil)")]
        public string Phone { get; set; }

        public List<Question> Questions { get; set; } = new();
        public Dictionary<int, string> Answers { get; set; } = new(); // Key = QuestionId
    }
}
