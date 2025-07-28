using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class EquipoAc
{
    public string NumActFijo { get; set; } = null!;

    public int ClaveTipoEquipo { get; set; }

    public string NumSerie { get; set; } = null!;

    public virtual CatTipoEquipo ClaveTipoEquipoNavigation { get; set; } = null!;

    public virtual Equipo NumActFijoNavigation { get; set; } = null!;
}
