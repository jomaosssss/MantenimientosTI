namespace MantenimientosTI.Models
{
    public class VMBitacora
    {
        public int IdRegistroActividad { get; set; } 
        public DateTime FechaHora { get; set; }
        public string Usuario { get; set; }
        public string RPE { get; set; }
        public string ClaveAccion { get; set; }
        public string DescripcionAccion { get; set; }
        public string DescripcionAdicional { get; set; }
        public string RolReal { get; set; }
        public string ZonaReal { get; set; }
        public string ClaveZonaReal { get; set; }
        public string DivisionReal { get; set; }
    }
}