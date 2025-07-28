using System;
using System.Collections.Generic;

namespace MantenimientosTI.Models;

public partial class Usuario
{
    public string Rpe { get; set; } = null!;

    public int ClaveRol { get; set; }

    public string ClaveDivision { get; set; } = null!;

    public string ClaveZona { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public string ApellidoP { get; set; } = null!;

    public string ApellidoM { get; set; } = null!;

    public string Correo { get; set; } = null!;

    public string Contrasenia { get; set; } = null!;

    public string Estatus { get; set; } = null!;

    public virtual CatZona CatZona { get; set; } = null!;

    public virtual CatRol ClaveRolNavigation { get; set; } = null!;

    public virtual ICollection<Mantenimiento> Mantenimientos { get; set; } = new List<Mantenimiento>();
}
