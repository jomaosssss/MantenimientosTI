using System;

namespace MantenimientosTI.Models.ViewModels
{
    public class VMAgendaImpresoraVista
    {
        public string NumActFijo { get; set; }
        public DateOnly FechaProgramada { get; set; }
        public string Zona { get; set; }
        public string Agencia { get; set; }
        public string Centro { get; set; }
        public string NumSerie { get; set; }          // De la tabla Impresora
        public string UsuarioReporta { get; set; }    // De ImpresoraMantenimiento (UsuarioReporta)
        public int ClaveAgenda { get; set; }          // De Agenda
        public string FolioAtencion { get; set; }     // De ImpresoraMantenimiento (FolioAtencion)
        public string Estatus { get; set; }           // De Agenda
        public string TipoMantenimiento { get; set; } // Para colorear la fila
        public string Responsable { get; set; }       // De Impresora (Responsable)
        public string Modelo { get; set; }            // De Impresora (Modelo)
        public string TipoImpresion { get; set; }     // De Impresora (TipoImpresion)
        public string IpImpresora { get; set; }       // De Impresora (IpImpresora)
    }
}