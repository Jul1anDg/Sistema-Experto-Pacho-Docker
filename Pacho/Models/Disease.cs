using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pacho.Models
{
    public class Disease
    {
        [Key] public int IdDisease { get; set; }

        [Required, Display(Name = "Nombre científico")]
        public string ScientificName { get; set; }

        [Required, Display(Name = "Nombre común")]
        public string CommonName { get; set; }

        public string Description { get; set; }
        public string Symptoms { get; set; }
        public string Conditions { get; set; }

        public string? ReferenceImage { get; set; } 
        
        [Column(TypeName = "varbinary(max)")]
        public byte[]? ReferenceImageEncrypted { get; set; }

        public string? ReferenceImageContentType { get; set; }

        public bool Asset { get; set; } = true;
        public DateTime CreationDate { get; set; } = DateTime.Now;
        public int TreatmentsTotal { get; set; } = 0;
    }
}