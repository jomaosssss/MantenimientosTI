using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models
{
    public class CatAccion
    {
        [Key]
        public int IdAccion { get; set; }

        [Required]
        [StringLength(50)]
        public string ClaveAccion { get; set; }

        [Required]
        [StringLength(255)]
        public string Descripcion { get; set; } // ← CAMBIA a "Descripcion"
    }
}