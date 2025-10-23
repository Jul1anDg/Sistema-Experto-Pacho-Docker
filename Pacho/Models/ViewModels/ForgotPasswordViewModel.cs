using System.ComponentModel.DataAnnotations;

namespace Pacho.Models.ViewModels
{
    public class ForgotPasswordViewModel
    {
        // Correo electrónico del usuario para recuperación de contraseña
        [Required, EmailAddress, Display(Name = "Correo")]
        public string Email { get; set; } = string.Empty;
    }
}
