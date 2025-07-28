using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class CatDivision
{
    public string ClaveDivision { get; set; } = null!;

    public string? NombreDivision { get; set; }

    public virtual ICollection<CatZona> CatZonas { get; set; } = new List<CatZona>();
}
