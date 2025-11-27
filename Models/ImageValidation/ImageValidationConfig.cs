using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models.ImageValidation
{
    [Table("ImageValidationConfig")]
    public class ImageValidationConfig
    {
        [Key]
        [StringLength(100)]
        public string ConfigKey { get; set; }

        [Required]
        [StringLength(255)]
        public string ConfigValue { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        public DateTime UpdatedAt { get; set; }

        [StringLength(100)]
        public string UpdatedBy { get; set; }

        // CONSTANTES PARA LAS CLAVES DE CONFIGURACIÓN
        public const string DuplicateThreshold = "DuplicateThreshold";
        public const string MaxImageSizeKB = "MaxImageSizeKB";
        public const string SimilarityThreshold = "SimilarityThreshold";
        public const string SearchMonthsBack = "SearchMonthsBack";
        public const string MaxSearchResults = "MaxSearchResults";
        public const string RiskScoreHigh = "RiskScoreHigh";
        public const string RiskScoreMedium = "RiskScoreMedium";
    }
}