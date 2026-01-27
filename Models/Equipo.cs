using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class Equipo
{
    public string NumActFijo { get; set; } = null!;

    public string ClaveDivision { get; set; } = null!;

    public string ClaveZona { get; set; } = null!;

    public string ClaveAgencia { get; set; } = null!;

    public string ClaveCentro { get; set; } = null!;

    public virtual ICollection<Agendum> Agenda { get; set; } = new List<Agendum>();

    public virtual CatCentro CatCentro { get; set; } = null!;

    public virtual ICollection<EquipoCfematico> EquipoCfematicos { get; set; } = new List<EquipoCfematico>();

    public virtual ICollection<Impresora> Impresoras { get; set; } = new List<Impresora>();

}
