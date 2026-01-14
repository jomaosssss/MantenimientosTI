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

    public decimal? LatitudAntes { get; set; }
    public decimal? LongitudAntes { get; set; }

    public decimal? LatitudDurante { get; set; }
    public decimal? LongitudDurante { get; set; }

    public decimal? LatitudDespues { get; set; }
    public decimal? LongitudDespues { get; set; }

    public bool EsDuplicadaAntes { get; set; }
    public bool EsDuplicadaDurante { get; set; }
    public bool EsDuplicadaDespues { get; set; }

    // Banderas de Fecha (1 = Fecha antigua o inconsistente)
    public bool AlertaFechaAntes { get; set; }
    public bool AlertaFechaDurante { get; set; }
    public bool AlertaFechaDespues { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.Now;

    public virtual Mantenimiento Mantenimiento { get; set; } = null!;
}
