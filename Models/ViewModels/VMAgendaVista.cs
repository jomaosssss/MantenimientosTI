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
    }
}
