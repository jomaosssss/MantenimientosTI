using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models
{
    [Table("ImpresoraMantenimiento")]
    public partial class ImpresoraMantenimiento
    {
        [Key]
        [StringLength(10)]
        public string FolioAtencion { get; set; } = null!;

        public int ClaveAgenda { get; set; }

        [Required]
        [StringLength(20)]
        public string NumActFijo { get; set; } = null!;

        [Column(TypeName = "date")]
        public DateTime FechaProgramada { get; set; }

        [Required]
        [StringLength(1)]
        public string ClaveTipoMtto { get; set; } = null!;

        [Required]
        [StringLength(30)]
        public string UsuarioReporta { get; set; } = null!;

        [Required]
        [StringLength(20)]
        public string Correo { get; set; } = null!;

        [Required]
        [StringLength(100)]
        public string PdfQueja { get; set; } = null!;

        [Required]
        [StringLength(300)]
        public string Problematica { get; set; } = null!;

        [Required]
        [StringLength(300)]
        public string Observaciones { get; set; } = null!;

        [Column(TypeName = "date")]
        public DateTime FechaReporte { get; set; }

        [Column(TypeName = "date")]
        public DateTime FechaGeneracion { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime FechaCaptura { get; set; }

        // Propiedades de navegación
        [ForeignKey("ClaveAgenda")]
        public virtual Agendum Agendum { get; set; } = null!;

        [ForeignKey("NumActFijo")]
        public virtual Equipo Equipo { get; set; } = null!;

        [ForeignKey("ClaveTipoMtto")]
        public virtual CatTipoMantenimiento CatTipoMantenimiento { get; set; } = null!;
    }
}