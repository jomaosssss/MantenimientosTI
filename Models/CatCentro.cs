using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class CatCentro
{
    public string ClaveDivision { get; set; } = null!;

    public string ClaveZona { get; set; } = null!;

    public string ClaveAgencia { get; set; } = null!;

    public string ClaveCentro { get; set; } = null!;

    public string? NombreCentro { get; set; }

    public virtual CatAgencium CatAgencium { get; set; } = null!;

    public virtual ICollection<Equipo> Equipos { get; set; } = new List<Equipo>();
}
