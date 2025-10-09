using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models
{
    [Table("configuracion")]
    public class Configuracion
    {
        [Key]
        [Column("claveConfiguracion")]
        public string ClaveConfiguracion { get; set; }

        [Required]
        [Column("valor")]
        public string Valor { get; set; }

        [Column("descripcion")]
        public string? Descripcion { get; set; }
    }
}