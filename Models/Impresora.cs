using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class Impresora
{
    public string NumActFijo { get; set; } = null!;
    public int ClaveTipoEquipo { get; set; }
    public string IdEquipo { get; set; } = null!;
    public string FolioLlave { get; set; } = null!;
    public string NumSerie { get; set; } = null!;
    public string Modelo { get; set; } = null!;
    public string TipoImpresion { get; set; } = null!;
    public string IpImpresora { get; set; } = null!;
    public string Responsable { get; set; } = null!;
    public string? Telefono { get; set; }
    public string? Extension { get; set; }
    public string? Correo { get; set; }

    // Propiedades de navegación
    public virtual Equipo NumActFijoNavigation { get; set; } = null!;
    public virtual CatTipoEquipo ClaveTipoEquipoNavigation { get; set; } = null!;
}