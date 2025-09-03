using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class Mantenimiento
{
    public int NumOrden { get; set; }

    public int ClaveAgenda { get; set; }

    public string NumActFijo { get; set; } = null!;

    //public DateOnly FechaProgramada { get; set; }

    public string ClaveTipoMtto { get; set; } = null!;

    public string Rpe { get; set; } = null!;

    public string? EvidenciaHojaServicio { get; set; }

    public string? Problemas { get; set; }

    public string? Diagnostico { get; set; }

    public string? Observaciones { get; set; }

    public DateTime FechaInsercion { get; set; } = DateTime.Now;

    public DateOnly FechaAtencion { get; set; } // NUEVO CAMPO

    public virtual Agendum Agendum { get; set; } = null!;

    public virtual Usuario RpeNavigation { get; set; } = null!;

    public virtual Equipo NumActFijoNavigation { get; set; } = null!; // NUEVA RELACIÓN

    public virtual CatTipoMantenimiento ClaveTipoMttoNavigation { get; set; } = null!; // NUEVA RELACIÓN

    public virtual ICollection<Foto> Fotos { get; set; } = new List<Foto>();
}
