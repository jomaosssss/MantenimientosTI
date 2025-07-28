using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class EquipoCfematico
{
    public string NumActFijo { get; set; } = null!;

    public string NumCajero { get; set; } = null!;

    public string NumSerie { get; set; } = null!;

    public string NumInventario { get; set; } = null!;

    public string IpCajero { get; set; } = null!;

    public virtual Equipo NumActFijoNavigation { get; set; } = null!;
}
