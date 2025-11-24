using System.ComponentModel.DataAnnotations;

namespace MantenimientosTI.Models.ImageValidation
{
    public class ImageValidationLog
    {
        public long Id { get; set; }
        public int FotoId { get; set; }
        public int MantenimientoId { get; set; }

        [Required]
        [StringLength(100)]
        public string UserId { get; set; }

        public string ValidationResult { get; set; } // JSON
        public int RiskScore { get; set; }

        [Required]
        [StringLength(20)]
        public string ValidationStatus { get; set; }

        public DateTime ValidatedAt { get; set; }

        [StringLength(100)]
        public string InspectorUserId { get; set; }
    }
}
