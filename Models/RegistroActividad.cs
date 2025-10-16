using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models
{
    public class RegistroActividad
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int IdRegistroActividad { get; set; }

        public DateTime FechaHora { get; set; }

        [Required]
        [StringLength(100)]
        public string Usuario { get; set; }

        //[StringLength(50)]
        //public string RPE { get; set; }

        [Required]
        [StringLength(50)]
        public string Accion { get; set; }

        [StringLength(500)]
        public string? Descripcion { get; set; }

        public int? IdEntidadAfectada { get; set; }
    }
}