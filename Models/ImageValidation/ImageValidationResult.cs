namespace MantenimientosTI.Models.ImageValidation
{
    public class ImageValidationResult
    {
        public bool IsValid { get; set; } = true;
        public int RiskScore { get; set; }
        public string PerceptualHash { get; set; }
        public string ValidationStatus { get; set; } = "Pending";
        public List<DuplicateMatch> Duplicates { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class DuplicateMatch
    {
        public int ExistingFotoId { get; set; }
        public int SimilarityScore { get; set; } // 0-100%
        public int HammingDistance { get; set; }
        public DateTime ExistingImageDate { get; set; }
        public int MantenimientoId { get; set; }
    }
}