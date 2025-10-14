using System;

namespace MantenimientosTI.Models
{
    public class VMBitacora
    {
        public DateTime FechaHora { get; set; }
        public string Usuario { get; set; }
        public string RPE { get; set; }
        public string ClaveAccion { get; set; }  // Desde CatAcciones
        public string DescripcionAccion { get; set; } // Desde CatAcciones
        public string DescripcionAdicional { get; set; } // Desde RegistroActividad
        public string RolReal { get; set; }
        public string ZonaReal { get; set; }
        public string ClaveZonaReal { get; set; }
        public string DivisionReal { get; set; }
    }
}