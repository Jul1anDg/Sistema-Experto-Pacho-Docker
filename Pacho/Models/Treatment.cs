// Models/Treatment.cs
using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Pacho.Models
{
    public partial class Treatment
    {
        [Key]
        public int IdTreatment { get; set; }

        [Required]
        [Display(Name = "Enfermedad")]
        public int DiseaseId { get; set; }


        [ValidateNever]
        public virtual Disease? Disease { get; set; }

        [Required]
        [Display(Name = "Experto")]
        public int ExpertId { get; set; }

      
        [ValidateNever]
        public virtual Expert? Expert { get; set; }

        [Required, MaxLength(100)]
        public string TreatmentType { get; set; } = string.Empty;

        [Required, MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? RecommendedProducts { get; set; }

        [MaxLength(200)]
        public string? Frequency { get; set; }

        [MaxLength(500)]
        public string? Precautions { get; set; }

        [MaxLength(200)]
        public string? WeatherConditions { get; set; }

        public int? DiasMejoriaVisual { get; set; }

        public bool Status { get; set; } = true;
        [Required(ErrorMessage = "Seleccione el entorno.")]
        [Display(Name = "Entorno")]
        [Range(1, 2, ErrorMessage = "Seleccione Hidroponía o Sustrato.")]
        public int? Environment { get; set; } 

        public DateTime CreationDate { get; set; } = DateTime.Now;
    }
}
