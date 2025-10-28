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

        // NUEVAS PROPIEDADES PARA MOTIVOS
        [StringLength(250, ErrorMessage = "La justificación no puede exceder los 250 caracteres.")]
        public string? MotivoCancelacion { get; set; }

        [StringLength(250, ErrorMessage = "La justificación no puede exceder los 250 caracteres.")]
        public string? JustificacionCancelacion { get; set; }

        public string? UsuarioSolicitudCancelacion { get; set; }
        public DateTime? FechaSolicitudCancelacion { get; set; }

        // Propiedad para mostrar fecha formateada
        public string FechaSolicitudFormateada =>
            FechaSolicitudCancelacion?.ToString("dd/MM/yyyy HH:mm") ?? "N/A";
    }
}