using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Pacho.Models.ViewModels
{
    public class ExpertRegistrationViewModel
    {
        // Correo electrónico del usuario; se valida formato y obligatoriedad
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        // Contraseña de acceso; mínimo 6 caracteres y tipo Password
        [Required, MinLength(6), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        // Nombre del usuario (obligatorio)
        [Required]
        public string Name { get; set; } = string.Empty;

        // Apellido del usuario (obligatorio)
        [Required]
        public string LastName { get; set; } = string.Empty;

        // Tipo de experiencia seleccionada (Empírica, Técnica, etc.)
        [Required]
        public string ExperienceType { get; set; } = string.Empty;

        // Años de experiencia; valor entre 0 y 100
        [Required, Range(0, 100)]
        public decimal ExperienceYears { get; set; }
    }
}
