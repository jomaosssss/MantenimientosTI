namespace MantenimientosTI.Models.ViewModels
{
    public class MantenimientoImpresoraViewModel
    {
        public string NumActFijo { get; set; }
        public string FolioAtencion { get; set; }
        public DateTime FechaReporte { get; set; }
        public string Problematica { get; set; }
        public string Rpe { get; set; }
        public string UsuarioReporta { get; set; }
        public string Correo { get; set; }
        public IFormFile? SoporteDocumental { get; set; }
        public string? Observaciones { get; set; }
    }
}
