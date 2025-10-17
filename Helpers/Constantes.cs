// En /Helpers/Constantes.cs
namespace MantenimientosTI.Helpers
{
    public static class BitacoraAcciones
    {
        public const string HabilitarCargaCsv = "HABILITAR_CARGA_CSV";
        public const string DeshabilitarCargaCsv = "DESHABILITAR_CARGA_CSV";
        public const string CrearUsuario = "CREAR_USUARIO";
        public const string ActualizarUsuario = "ACTUALIZAR_USUARIO";
        public const string InicioSesionAdmin = "INICIO_SESION_ADMIN";
        public const string InicioSesionTecnico = "INICIO_SESION_TECNICO";  // ✅ CORREGIDO
        public const string LogoutSistema = "LOGOUT_SISTEMA";
        public const string CargaCsv = "CARGA_CSV";
        public const string CargaMttoCorrectivo = "CARGA_MTTO_CORRECTIVO";
        public const string PreCancelacionMtto = "PRECANCELACION_MTTO";
        public const string ConfirmacionCancelacion = "CONFIRMACION_CANCELACION";
        public const string TerminacionMtto = "TERMINACION_MTTO";
    }
}