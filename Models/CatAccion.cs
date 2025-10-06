using System.ComponentModel.DataAnnotations;

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
        public string Descripcion { get; set; }
    }
}