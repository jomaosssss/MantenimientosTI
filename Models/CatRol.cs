using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class CatRol
{
    public int ClaveRol { get; set; }

    public string? Nombre { get; set; }

    public virtual ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}
