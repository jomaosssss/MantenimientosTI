// ViewModels/VMAgendaVista.cs - ACTUALIZADO
using System.ComponentModel.DataAnnotations;

namespace MantenimientosTI.Models.ViewModels
{
    public class VMAgendaVista
    {
        public string NumActFijo { get; set; }
        public DateOnly FechaProgramada { get; set; }
        public string Zona { get; set; }
        public string Agencia { get; set; }
        public string Centro { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string Estatus { get; set; }
        public string NumCajero { get; set; }
        public string TipoMantenimiento { get; set; }
        public string UsuarioAsignado { get; set; }
        public string NumSerie { get; set; } // Para impresoras
        public string UsuarioReporta { get; set; } // Para impresoras
        public string FolioAtencion { get; set; } // Para impresoras
        public DateOnly FechaReporte { get; set; } // NUEVO
        public DateOnly FechaGeneracion { get; set; }
        public int ClaveAgenda { get; set; }

        // NUEVO: Para referencia interna
        public int ClaveMotivo { get; set; }

        // PROPIEDADES PARA MOTIVOS (ahora desde catálogo)
        public string? MotivoCancelacion { get; set; }

        [StringLength(500, ErrorMessage = "La justificación no puede exceder los 500 caracteres.")]
        public string? JustificacionCancelacion { get; set; }

        // AHORA SON RPEs (varchar(5))
        public string? UsuarioSolicitudCancelacion { get; set; }
        public DateTime? FechaSolicitudCancelacion { get; set; }
        public string? UsuarioAprobacionCancelacion { get; set; }
        public DateTime? FechaAprobacionCancelacion { get; set; }

        public string FechaSolicitudFormateada =>
            FechaSolicitudCancelacion?.ToString("dd/MM/yyyy HH:mm") ?? "N/A";
    }
}
