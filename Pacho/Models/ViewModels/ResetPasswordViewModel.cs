using System.ComponentModel.DataAnnotations;

namespace Pacho.Models.ViewModels
{
    public class ResetPasswordViewModel
    {
        // Token único enviado al correo del usuario para validar la recuperación
        [Required]
        public string Token { get; set; } = string.Empty;

        // Nueva contraseña definida por el usuario; mínimo 8 caracteres
        [Required, DataType(DataType.Password), MinLength(8)]
        [Display(Name = "Nueva contraseña")]
        public string NewPassword { get; set; } = string.Empty;

        // Confirmación de la nueva contraseña; debe coincidir con NewPassword
        [Required, DataType(DataType.Password), Compare(nameof(NewPassword))]
        [Display(Name = "Confirmar contraseña")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
