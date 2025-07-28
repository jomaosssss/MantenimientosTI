using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class CatEvento
{
    public string ClaveEvento { get; set; } = null!;

    public string ClaveFalla { get; set; } = null!;

    public string Fuente { get; set; } = null!;

    public string Descripcion { get; set; } = null!;

    public int Severidad { get; set; }

    public virtual CatFalla ClaveFallaNavigation { get; set; } = null!;
}
