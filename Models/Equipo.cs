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

    // Propiedades de navegación existentes
    public virtual ICollection<Agendum> Agenda { get; set; } = new List<Agendum>();
    public virtual CatCentro CatCentro { get; set; } = null!;
    public virtual ICollection<EquipoCfematico> EquipoCfematicos { get; set; } = new List<EquipoCfematico>();

    // AGREGAR ESTAS PROPIEDADES PARA LAS NUEVAS RELACIONES:

    // Relación uno a uno con Impresora (una impresora por equipo)
    public virtual Impresora Impresora { get; set; } = null!;

    // Relación uno a muchos con ImpresoraMantenimiento (un equipo puede tener múltiples mantenimientos)
    public virtual ICollection<ImpresoraMantenimiento> ImpresoraMantenimientos { get; set; } = new List<ImpresoraMantenimiento>();
}