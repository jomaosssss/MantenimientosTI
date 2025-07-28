using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class CatTipoMantenimiento
{
    public string ClaveTipoMtto { get; set; } = null!;

    public string NombreTipoM { get; set; } = null!;

    public virtual ICollection<Agendum> Agenda { get; set; } = new List<Agendum>();
}
