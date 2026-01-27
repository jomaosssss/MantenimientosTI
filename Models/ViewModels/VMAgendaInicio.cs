namespace MantenimientosTI.Models.ViewModels
{
    public class VMAgendaInicio
    {
        public List<VMAgendaVista> Cfematicos { get; set; } = new();
        public List<VMAgendaVista> AtencionClientes { get; set; } = new();
        public List<VMAgendaVista> Computo { get; set; } = new();
        public List<VMAgendaImpresoraVista> Impresoras { get; set; } = new();
        public int TerminadosCount { get; set; }
        public int PendientesCount { get; set; }
        public int ProgramadosCount { get; set; }
        public int ProgramadosProximoMesCount { get; set; }
        public int PendientesMesAnteriorCount { get; set; }
    }
}