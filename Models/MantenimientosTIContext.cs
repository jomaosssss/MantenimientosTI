using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace MantenimientosTI.Models;

public partial class MantenimientosTIContext : DbContext
{
    public MantenimientosTIContext()
    {
    }

    public MantenimientosTIContext(DbContextOptions<MantenimientosTIContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Agendum> Agenda { get; set; }

    public virtual DbSet<CatAgencium> CatAgencia { get; set; }

    public virtual DbSet<CatCentro> CatCentros { get; set; }

    public virtual DbSet<CatDivision> CatDivisions { get; set; }

    public virtual DbSet<CatEvento> CatEventos { get; set; }

    public virtual DbSet<CatFalla> CatFallas { get; set; }

    public virtual DbSet<CatRol> CatRols { get; set; }

    public virtual DbSet<CatTipoEquipo> CatTipoEquipos { get; set; }

    public virtual DbSet<CatTipoMantenimiento> CatTipoMantenimientos { get; set; }

    public virtual DbSet<CatZona> CatZonas { get; set; }

    public virtual DbSet<Configuracion> Configuraciones { get; set; }

    public virtual DbSet<Equipo> Equipos { get; set; }

    public virtual DbSet<EquipoAc> EquipoAcs { get; set; }

    public virtual DbSet<EquipoCfematico> EquipoCfematicos { get; set; }

    public virtual DbSet<EquipoComputo> EquipoComputos { get; set; }

    public virtual DbSet<Foto> Fotos { get; set; }

    public virtual DbSet<Mantenimiento> Mantenimientos { get; set; }

    public virtual DbSet<RegistroEvento> RegistroEventos { get; set; }

    public virtual DbSet<Usuario> Usuarios { get; set; }

//    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
//#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
//        => optionsBuilder.UseSqlServer("Server=MATEBOOKD14;Database=MantenimientosTI;Trusted_Connection=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agendum>(entity =>
        {
            entity.HasKey(e => e.ClaveAgenda).HasName("PK_Agenda_C34F139CA770DDB7");

            entity.Property(e => e.ClaveAgenda)
                .ValueGeneratedOnAdd()
                .HasColumnName("claveAgenda");
            entity.Property(e => e.NumActFijo)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numActFijo");
            entity.Property(e => e.FechaProgramada).HasColumnName("fechaProgramada");
            entity.Property(e => e.ClaveTipoMtto)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveTipoMtto");
            entity.Property(e => e.Estatus)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("estatus");

            entity.HasOne(d => d.ClaveTipoMttoNavigation).WithMany(p => p.Agenda)
                .HasForeignKey(d => d.ClaveTipoMtto)
                .HasConstraintName("FK_Agenda_claveTp_03FD984C");

            entity.HasOne(d => d.NumActFijoNavigation).WithMany(p => p.Agenda)
                .HasForeignKey(d => d.NumActFijo)
                .HasConstraintName("FK_Agenda_numActF1_02FC7413");
        });

        modelBuilder.Entity<CatAgencium>(entity =>
        {
            entity.HasKey(e => new { e.ClaveDivision, e.ClaveZona, e.ClaveAgencia }).HasName("PK__catAgenc__AE5FB5D1A4710C64");

            entity.ToTable("catAgencia");

            entity.Property(e => e.ClaveDivision)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveDivision");
            entity.Property(e => e.ClaveZona)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveZona");
            entity.Property(e => e.ClaveAgencia)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveAgencia");
            entity.Property(e => e.NombreAgencia)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("nombreAgencia");

            entity.HasOne(d => d.CatZona).WithMany(p => p.CatAgencia)
                .HasForeignKey(d => new { d.ClaveDivision, d.ClaveZona })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__catAgencia__29572725");
        });

        modelBuilder.Entity<CatCentro>(entity =>
        {
            entity.HasKey(e => new { e.ClaveDivision, e.ClaveZona, e.ClaveAgencia, e.ClaveCentro }).HasName("PK__catCentr__E94BBF8BC24EC3EC");

            entity.ToTable("catCentro");

            entity.Property(e => e.ClaveDivision)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveDivision");
            entity.Property(e => e.ClaveZona)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveZona");
            entity.Property(e => e.ClaveAgencia)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveAgencia");
            entity.Property(e => e.ClaveCentro)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveCentro");
            entity.Property(e => e.NombreCentro)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("nombreCentro");

            entity.HasOne(d => d.CatAgencium).WithMany(p => p.CatCentros)
                .HasForeignKey(d => new { d.ClaveDivision, d.ClaveZona, d.ClaveAgencia })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__catCentro__2C3393D0");
        });

        modelBuilder.Entity<CatDivision>(entity =>
        {
            entity.HasKey(e => e.ClaveDivision).HasName("PK__catDivis__9924DDB6CEBB7688");

            entity.ToTable("catDivision");

            entity.Property(e => e.ClaveDivision)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveDivision");
            entity.Property(e => e.NombreDivision)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("nombreDivision");
        });

        modelBuilder.Entity<CatEvento>(entity =>
        {
            entity.HasKey(e => e.ClaveEvento).HasName("PK__catEvent__FA84FFE17739B9C4");

            entity.ToTable("catEvento");

            entity.Property(e => e.ClaveEvento)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("claveEvento");
            entity.Property(e => e.ClaveFalla)
                .HasMaxLength(1)
                .IsUnicode(false)
                .HasColumnName("claveFalla");
            entity.Property(e => e.Descripcion)
                .HasMaxLength(256)
                .IsUnicode(false)
                .HasColumnName("descripcion");
            entity.Property(e => e.Fuente)
                .HasMaxLength(256)
                .IsUnicode(false)
                .HasColumnName("fuente");
            entity.Property(e => e.Severidad)
                .HasColumnName("severidad");
            entity.Property(e => e.Importancia)
                .HasColumnName("importancia");


            entity.HasOne(d => d.ClaveFallaNavigation).WithMany(p => p.CatEventos)
                .HasForeignKey(d => d.ClaveFalla)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__catEvento__sever__3F466844");
        });

        modelBuilder.Entity<CatFalla>(entity =>
        {
            entity.HasKey(e => e.ClaveFalla).HasName("PK__catFalla__5E9B5304C1B1233C");

            entity.ToTable("catFalla");

            entity.Property(e => e.ClaveFalla)
                .HasMaxLength(1)
                .IsUnicode(false)
                .HasColumnName("claveFalla");
            entity.Property(e => e.Descripcion)
                .HasMaxLength(80)
                .IsUnicode(false)
                .HasColumnName("descripcion");
        });

        modelBuilder.Entity<CatRol>(entity =>
        {
            entity.HasKey(e => e.ClaveRol).HasName("PK__catRol__C0E0123BF8E20AF1");

            entity.ToTable("catRol");

            entity.Property(e => e.ClaveRol)
                .ValueGeneratedNever()
                .HasColumnName("claveRol");
            entity.Property(e => e.Nombre)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("nombre");
        });

        modelBuilder.Entity<CatTipoEquipo>(entity =>
        {
            entity.HasKey(e => e.ClaveTipoEquipo).HasName("PK__catTipoE__B5D92AF53AECB17B");

            entity.ToTable("catTipoEquipo");

            entity.Property(e => e.ClaveTipoEquipo)
                .ValueGeneratedNever()
                .HasColumnName("claveTipoEquipo");
            entity.Property(e => e.NombreTipoEquipo)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("nombreTipoEquipo");
        });

        modelBuilder.Entity<CatTipoMantenimiento>(entity =>
        {
            entity.HasKey(e => e.ClaveTipoMtto).HasName("PK__catTipoM__000901B966059F0D");

            entity.ToTable("catTipoMantenimiento");

            entity.Property(e => e.ClaveTipoMtto)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveTipoMtto");
            entity.Property(e => e.NombreTipoM)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("nombreTipoM");
        });

        modelBuilder.Entity<CatZona>(entity =>
        {
            entity.HasKey(e => new { e.ClaveDivision, e.ClaveZona }).HasName("PK__catZona__D48FE80366C5F9F8");

            entity.ToTable("catZona");

            entity.Property(e => e.ClaveDivision)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveDivision");
            entity.Property(e => e.ClaveZona)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveZona");
            entity.Property(e => e.NombreZona)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("nombreZona");

            entity.HasOne(d => d.ClaveDivisionNavigation).WithMany(p => p.CatZonas)
                .HasForeignKey(d => d.ClaveDivision)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__catZona__claveDi__267ABA7A");
        });

        modelBuilder.Entity<Configuracion>(entity =>
        {
            entity.ToTable("Configuracion");

            entity.HasKey(e => e.ClaveConfiguracion);

            entity.Property(e => e.ClaveConfiguracion)
                .HasColumnName("claveConfiguracion")
                .HasMaxLength(100)
                .IsUnicode(false);

            entity.Property(e => e.Valor)
                .HasColumnName("valor")
                .HasMaxLength(255)
                .IsUnicode(false)
                .IsRequired();

            entity.Property(e => e.Descripcion)
                .HasColumnName("descripcion")
                .HasMaxLength(500)
                .IsRequired(false);
        });

        modelBuilder.Entity<Equipo>(entity =>
        {
            entity.HasKey(e => e.NumActFijo).HasName("PK__Equipo__CE6D8AC9B3623CFF");

            entity.ToTable("Equipo");

            entity.Property(e => e.NumActFijo)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numActFijo");
            entity.Property(e => e.ClaveAgencia)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveAgencia");
            entity.Property(e => e.ClaveCentro)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveCentro");
            entity.Property(e => e.ClaveDivision)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveDivision");
            entity.Property(e => e.ClaveZona)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveZona");

            entity.HasOne(d => d.CatCentro).WithMany(p => p.Equipos)
                .HasForeignKey(d => new { d.ClaveDivision, d.ClaveZona, d.ClaveAgencia, d.ClaveCentro })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Equipo__36B12243");
        });

        modelBuilder.Entity<EquipoAc>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("EquipoAC");

            entity.Property(e => e.ClaveTipoEquipo).HasColumnName("claveTipoEquipo");
            entity.Property(e => e.NumActFijo)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numActFijo");
            entity.Property(e => e.NumSerie)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numSerie");

            entity.HasOne(d => d.ClaveTipoEquipoNavigation).WithMany()
                .HasForeignKey(d => d.ClaveTipoEquipo)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__EquipoAC__claveT__44FF419A");

            entity.HasOne(d => d.NumActFijoNavigation).WithMany()
                .HasForeignKey(d => d.NumActFijo)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__EquipoAC__numAct__440B1D61");
        });

        modelBuilder.Entity<EquipoCfematico>(entity =>
        {
            entity.HasKey(e => e.NumCajero).HasName("PK__EquipoCF__45C5A10EE3A6AC27");

            entity.ToTable("EquipoCFEmatico");

            entity.Property(e => e.NumCajero)
                .HasMaxLength(3)
                .IsUnicode(false)
                .HasColumnName("numCajero");
            entity.Property(e => e.IpCajero)
                .HasMaxLength(15)
                .IsUnicode(false)
                .HasColumnName("ipCajero");
            entity.Property(e => e.NumActFijo)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numActFijo");
            entity.Property(e => e.NumInventario)
                .HasMaxLength(8)
                .IsUnicode(false)
                .HasColumnName("numInventario");
            entity.Property(e => e.NumSerie)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numSerie");
            entity.Property(e => e.Version)
                .HasMaxLength(10)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("version");

            entity.HasOne(d => d.NumActFijoNavigation).WithMany(p => p.EquipoCfematicos)
                .HasForeignKey(d => d.NumActFijo)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__EquipoCFE__numAc__3A81B327");
        });

        modelBuilder.Entity<EquipoComputo>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("EquipoComputo");

            entity.Property(e => e.ClaveTipoEquipo).HasColumnName("claveTipoEquipo");
            entity.Property(e => e.NombreRpe)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("nombreRPE");
            entity.Property(e => e.NumActFijo)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numActFijo");
            entity.Property(e => e.NumSerieMonitor)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numSerieMonitor");
            entity.Property(e => e.NumSeriePc)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numSeriePC");
            entity.Property(e => e.Rpe)
                .HasMaxLength(5)
                .IsUnicode(false)
                .HasColumnName("RPE");

            entity.HasOne(d => d.ClaveTipoEquipoNavigation).WithMany()
                .HasForeignKey(d => d.ClaveTipoEquipo)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__EquipoCom__clave__48CFD27E");

            entity.HasOne(d => d.NumActFijoNavigation).WithMany()
                .HasForeignKey(d => d.NumActFijo)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__EquipoCom__numAc__47DBAE45");
        });

        modelBuilder.Entity<Foto>(entity =>
        {
            entity.ToTable("Foto"); // Esto es lo más importante

            entity.HasKey(e => new { e.NumOrden, e.FechaHora }); // Clave compuesta (opcional)

            entity.Property(e => e.NumOrden)
                .HasColumnName("numOrden");

            entity.Property(e => e.FotoAntes)
                .HasColumnName("fotoAntes");

            entity.Property(e => e.FotoDurante)
                .HasColumnName("fotoDurante");

            entity.Property(e => e.FotoDespues)
                .HasColumnName("fotoDespues");

            entity.Property(e => e.FechaHora)
                .HasColumnName("fechaHora")
                .HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<Mantenimiento>(entity =>
        {
            entity.ToTable("Mantenimiento");

            entity.HasKey(e => e.NumOrden).HasName("PK_Mantenim_03BED0813500DED1");

            entity.Property(e => e.NumOrden)
                .HasColumnName("numOrden")
                .ValueGeneratedNever();

            entity.Property(e => e.ClaveAgenda)
                .HasColumnName("claveAgenda");

            entity.Property(e => e.NumActFijo)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("numActFijo");

            entity.Property(e => e.ClaveTipoMtto)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveTipoMtto");

            entity.Property(e => e.Rpe)
                .HasMaxLength(5)
                .IsUnicode(false)
                .HasColumnName("RPE");

            entity.Property(e => e.EvidenciaHojaServicio)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("evidenciaHojaServicio");

            entity.Property(e => e.Problemas)
                .HasMaxLength(300)
                .IsUnicode(false)
                .HasColumnName("problemas");

            entity.Property(e => e.Diagnostico)
                .HasMaxLength(300)
                .IsUnicode(false)
                .HasColumnName("diagnostico");

            entity.Property(e => e.Observaciones)
                .HasMaxLength(300)
                .IsUnicode(false)
                .HasColumnName("observaciones");

            entity.Property(e => e.FechaInsercion)
                .HasColumnName("fechaInsercion")
                .HasDefaultValueSql("(getdate())");

            entity.Property(e => e.FechaAtencion)
                .HasColumnName("fechaAtencion");

            // Relaciones con nombres exactos de constraints
            entity.HasOne(d => d.Agendum)
                .WithMany(p => p.Mantenimientos)
                .HasForeignKey(d => d.ClaveAgenda)
                .HasConstraintName("FK_Mantenimi_clave_07C12930");

            entity.HasOne(d => d.RpeNavigation)
                .WithMany(p => p.Mantenimientos)
                .HasForeignKey(d => d.Rpe)
                .HasConstraintName("FK_Mantenimien_RPE_09A971A2");

            entity.HasOne(d => d.NumActFijoNavigation)
                .WithMany()
                .HasForeignKey(d => d.NumActFijo)
                .HasConstraintName("FK_Mantenimi_numAct_08B6AD69");

            entity.HasOne(d => d.ClaveTipoMttoNavigation)
                .WithMany()
                .HasForeignKey(d => d.ClaveTipoMtto)
                .HasConstraintName("FK_Mantenimi_clave_0A9D95DB");

            entity.HasMany(d => d.Fotos)
                .WithOne(p => p.Mantenimiento)
                .HasForeignKey(d => d.NumOrden)
                .HasConstraintName("FK_Foto_Mantenimiento");
        });

        modelBuilder.Entity<RegistroEvento>(entity =>
        {
            entity.HasNoKey();

            entity.Property(e => e.ClaveEvento)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("claveEvento");
            entity.Property(e => e.FechaEvento)
                .HasColumnType("datetime")
                .HasColumnName("fechaEvento");
            entity.Property(e => e.NumCajero)
                .HasMaxLength(3)
                .IsUnicode(false)
                .HasColumnName("numCajero");

            entity.HasOne(d => d.ClaveEventoNavigation)
                .WithMany()
                .HasForeignKey(d => d.ClaveEvento)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__RegistroE__clave__4222D4EF");

            // CAMBIO PRINCIPAL: Actualizar el comportamiento de eliminación
            entity.HasOne(d => d.NumCajeroNavigation)
                .WithMany()
                .HasForeignKey(d => d.NumCajero)
                .OnDelete(DeleteBehavior.Cascade)  // Cambiado de ClientSetNull a Cascade
                .HasConstraintName("FK_RegistroEventos_CFEmatico");
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(e => e.Rpe).HasName("PK__Usuario__CAFF7995E43DC2FF");

            entity.ToTable("Usuario");

            entity.Property(e => e.Rpe)
                .HasMaxLength(5)
                .IsUnicode(false)
                .HasColumnName("RPE");
            entity.Property(e => e.ApellidoM)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("apellidoM");
            entity.Property(e => e.ApellidoP)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("apellidoP");
            entity.Property(e => e.ClaveDivision)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveDivision");
            entity.Property(e => e.ClaveRol).HasColumnName("claveRol");
            entity.Property(e => e.ClaveZona)
                .HasMaxLength(2)
                .IsUnicode(false)
                .IsFixedLength()
                .HasColumnName("claveZona");
            entity.Property(e => e.Contrasenia)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("contrasenia");
            entity.Property(e => e.Correo)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasColumnName("correo");
            entity.Property(e => e.Estatus)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("estatus");
            entity.Property(e => e.Nombre)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("nombre");
            entity.Property(e => e.RecibirReporte)
                .HasMaxLength(2)
                .IsUnicode(false)
                .HasColumnName("recibirReporte");

            entity.HasOne(d => d.ClaveRolNavigation).WithMany(p => p.Usuarios)
                .HasForeignKey(d => d.ClaveRol)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Usuario__claveRo__30F848ED");

            entity.HasOne(d => d.CatZona).WithMany(p => p.Usuarios)
                .HasForeignKey(d => new { d.ClaveDivision, d.ClaveZona })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Usuario__31EC6D26");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
