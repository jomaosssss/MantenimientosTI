using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MantenimientosTI.Models
{
    public class EvidenciaMantenimiento
    {
        [Key]
        public int IdEvidencia { get; set; }
        public int NumOrden { get; set; }
        public string Etapa { get; set; } = null!; // "ANTES", "DURANTE", "DESPUES"
        public string RutaFoto { get; set; } = null!;

        // Datos Forenses
        public string? HashVisual { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        public DateTime? FechaCaptura { get; set; }

        // Resultado Auditoría
        public bool EsSospechosa { get; set; }
        public string? TipoAnomalia { get; set; } // "DUPLICADO", "FECHA", etc.
        public DateTime FechaSubida { get; set; } = DateTime.Now;

        [ForeignKey("NumOrden")]
        public virtual Mantenimiento Mantenimiento { get; set; } = null!;
    }
}