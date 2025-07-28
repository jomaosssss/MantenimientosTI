using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class Agendum
{
    public int ClaveAgenda { get; set; }

    public string NumActFijo { get; set; } = null!;

    public string ClaveTipoMtto { get; set; } = null!;

    public DateOnly FechaProgramada { get; set; }

    public string Estatus { get; set; } = null!;

    public virtual CatTipoMantenimiento ClaveTipoMttoNavigation { get; set; } = null!;

    public virtual ICollection<Mantenimiento> Mantenimientos { get; set; } = new List<Mantenimiento>();

    public virtual Equipo NumActFijoNavigation { get; set; } = null!;
}
