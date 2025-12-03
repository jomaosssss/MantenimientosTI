using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class Foto
{
    public int NumOrden { get; set; }

    public string? FotoAntes { get; set; }

    public string? FotoDurante { get; set; }

    public string? FotoDespues { get; set; }

    public string? HashAntes { get; set; }

    public string? HashDurante { get; set; }

    public string? HashDespues { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.Now;

    public virtual Mantenimiento Mantenimiento { get; set; } = null!;
}
