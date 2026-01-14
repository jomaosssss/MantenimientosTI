using MantenimientosTI.Models;

namespace MantenimientosTI.Models.ViewModels
{
    public class DetalleReporteViewModel
    {
        public Mantenimiento Mantenimiento { get; set; }
        public Foto? FotoInfo { get; set; }

        // Datos visuales existentes
        public string NombreUsuario { get; set; }
        public string FechaProgramada { get; set; }
        public string TipoEquipo { get; set; }
        public string NumCajero { get; set; }
        public string Zona { get; set; }
        public string Agencia { get; set; }
        public string Centro { get; set; }

        // --- NUEVO: DATOS DEL ORIGEN DEL FRAUDE ---
        public InfoOrigenFraude? OrigenAntes { get; set; }
        public InfoOrigenFraude? OrigenDurante { get; set; }
        public InfoOrigenFraude? OrigenDespues { get; set; }
    }

    // Clase auxiliar para guardar los datos del "Paciente Cero"
    public class InfoOrigenFraude
    {
        public int NumOrden { get; set; }
        public string NombreTecnico { get; set; } = "";
        public string Rpe { get; set; } = "";
        public string Fecha { get; set; } = "";
        public string Zona { get; set; } = "";
    }
}