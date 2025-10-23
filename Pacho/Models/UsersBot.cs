using System;

namespace Pacho.Models
{
    public partial class UsersBot
    {
        public long IdUserbot { get; set; }  // Identificador único del usuario del bot

        public string TelegramId { get; set; } = null!;  // ID único del usuario en Telegram

        public string? Phone { get; set; }  // Teléfono del usuario (opcional)

        public int? TotalDiagnoses { get; set; }  // Número total de diagnósticos realizados por el usuario

        public string? AgreementStatus { get; set; }  // Indica si el usuario aceptó los términos o acuerdo de uso

        public DateTime? DateAgreement { get; set; }  // Fecha en la que el usuario aceptó el acuerdo

        public DateTime? LastUpdated { get; set; }  // Fecha de la última actualización de los datos del usuario

        public bool? RecommendationState { get; set; }  // Estado de las recomendaciones (por ejemplo: "pendiente", "enviado")

        public DateTime? RecommendationDate { get; set; }  // Fecha en la que se realizó la última recomendación

        public DateTime? CreatedAt { get; set; }  // Fecha de creación del registro en la base de datos
    }
}
