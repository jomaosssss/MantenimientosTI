using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class ImpresoraMantenimiento
{
    public string FolioAtencion { get; set; } = null!;
    public int ClaveAgenda { get; set; }
    public string Rpe { get; set; } = null!;
    public string UsuarioReporta { get; set; } = null!;
    public string Correo { get; set; } = null!;
    public string PdfQueja { get; set; } = null!;
    public string Problematica { get; set; } = null!;
    public string Observaciones { get; set; } = null!;
    public DateOnly FechaReporte { get; set; }
    public DateTime FechaCaptura { get; set; }

    // Propiedades de navegación
    public virtual Agendum ClaveAgendaNavigation { get; set; } = null!;
}