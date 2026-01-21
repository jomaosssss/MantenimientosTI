using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class CatZona
{
    public string ClaveDivision { get; set; } = null!;

    public string ClaveZona { get; set; } = null!;

    public string? NombreZona { get; set; }

    public virtual ICollection<CatAgencium> CatAgencia { get; set; } = new List<CatAgencium>();

    public virtual CatDivision ClaveDivisionNavigation { get; set; } = null!;

    public virtual ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
    public virtual ICollection<Refaccion> Refacciones { get; set; }
}
