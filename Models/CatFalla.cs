using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class CatFalla
{
    public string ClaveFalla { get; set; } = null!;

    public string Descripcion { get; set; } = null!;

    public virtual ICollection<CatEvento> CatEventos { get; set; } = new List<CatEvento>();
}
