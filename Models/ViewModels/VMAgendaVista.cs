// ViewModels/VMAgendaVista.cs
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
        public int ClaveAgenda { get; set; }

        // PROPIEDADES PARA MOTIVOS (mismos tamaños que la tabla original)
        [StringLength(100, ErrorMessage = "El motivo no puede exceder los 100 caracteres.")]
        public string? MotivoCancelacion { get; set; }

        [StringLength(500, ErrorMessage = "La justificación no puede exceder los 500 caracteres.")]
        public string? JustificacionCancelacion { get; set; }

        public string? UsuarioSolicitudCancelacion { get; set; }
        public DateTime? FechaSolicitudCancelacion { get; set; }
        public string? UsuarioAprobacionCancelacion { get; set; }
        public DateTime? FechaAprobacionCancelacion { get; set; }

        public string FechaSolicitudFormateada =>
            FechaSolicitudCancelacion?.ToString("dd/MM/yyyy HH:mm") ?? "N/A";
    }
}