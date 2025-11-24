using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models.ImageValidation
{
    public class ImageFingerprint
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [StringLength(64)]
        public string PerceptualHash { get; set; }

        [Required]
        [StringLength(100)]
        public string UserId { get; set; } // RPE

        public int MantenimientoId { get; set; } // numOrden
        public int FotoId { get; set; }

        [Required]
        [StringLength(10)]
        public string TipoFoto { get; set; } // Antes, Durante, Despues

        public DateTime CreatedAt { get; set; }
    }
}
