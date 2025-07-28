using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class RegistroEvento
{
    public string NumCajero { get; set; } = null!;

    public string ClaveEvento { get; set; } = null!;

    public DateTime FechaEvento { get; set; }

    public virtual CatEvento ClaveEventoNavigation { get; set; } = null!;

    public virtual EquipoCfematico NumCajeroNavigation { get; set; } = null!;
}
