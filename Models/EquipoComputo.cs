using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class EquipoComputo
{
    public string NumActFijo { get; set; } = null!;

    public int ClaveTipoEquipo { get; set; }

    public string? NumSeriePc { get; set; }

    public string? NumSerieMonitor { get; set; }

    public string? Rpe { get; set; }

    public string? NombreRpe { get; set; }

    public virtual CatTipoEquipo ClaveTipoEquipoNavigation { get; set; } = null!;

    public virtual Equipo NumActFijoNavigation { get; set; } = null!;
}
