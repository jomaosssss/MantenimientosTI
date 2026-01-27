using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models
{
    [Table("Impresora")]
    public partial class Impresora
    {
        [Key]
        [StringLength(20)]
        public string NumActFijo { get; set; } = null!;

        public int ClaveTipoEquipo { get; set; }

        [Required]
        [StringLength(20)]
        public string IdEquipo { get; set; } = null!;

        [Required]
        [StringLength(10)]
        public string FolioLlave { get; set; } = null!;

        [Required]
        [StringLength(20)]
        public string NumSerie { get; set; } = null!;

        [Required]
        [StringLength(20)]
        public string Modelo { get; set; } = null!;

        [Required]
        [StringLength(5)]
        public string TipoImpresion { get; set; } = null!;

        [Required]
        [StringLength(15)]
        public string IpImpresora { get; set; } = null!;

        [Required]
        [StringLength(30)]
        public string Empresa { get; set; } = null!;

        [Required]
        [StringLength(30)]
        [Column("coordinacionGerencia")] // <-- CAMBIADO: coordinacion (con "ción")
        public string CoordinacionGerencia { get; set; } = null!;


        [Required]
        [StringLength(50)]
        public string Responsable { get; set; } = null!;

        [StringLength(30)]
        public string? Telefono { get; set; }

        [StringLength(40)]
        public string? Correo { get; set; }

        // Propiedades de navegación
        [ForeignKey("NumActFijo")]
        public virtual Equipo Equipo { get; set; } = null!;

        [ForeignKey("ClaveTipoEquipo")]
        public virtual CatTipoEquipo CatTipoEquipo { get; set; } = null!;
    }
}
