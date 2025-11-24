using System.ComponentModel.DataAnnotations;

namespace MantenimientosTI.Models.ImageValidation
{
    public class SuspiciousImageAlert
    {
        public long Id { get; set; }
        public int FotoId { get; set; }
        public int MantenimientoId { get; set; }

        [Required]
        [StringLength(100)]
        public string UserId { get; set; }

        [Required]
        [StringLength(500)]
        public string Reason { get; set; }

        [Required]
        [StringLength(20)]
        public string RiskLevel { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; }

        public DateTime CreatedAt { get; set; }

        [StringLength(100)]
        public string ResolvedBy { get; set; }

        public DateTime? ResolvedAt { get; set; }

        [StringLength(500)]
        public string ResolutionNotes { get; set; }
    }
}