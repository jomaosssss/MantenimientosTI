using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class CatAgencium
{
    public string ClaveDivision { get; set; } = null!;

    public string ClaveZona { get; set; } = null!;

    public string ClaveAgencia { get; set; } = null!;

    public string? NombreAgencia { get; set; }

    public virtual ICollection<CatCentro> CatCentros { get; set; } = new List<CatCentro>();

    public virtual CatZona CatZona { get; set; } = null!;
}
