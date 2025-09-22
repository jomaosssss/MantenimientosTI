// MantenimientosTI/Models/ViewModels/ApiAuthModels.cs
namespace MantenimientosTI.Models.ViewModels
{
    // Clase para ENVIAR la solicitud a la API
    public class ApiLoginRequest
    {
        public string rpe { get; set; }
        public string contrasenia { get; set; }
    }

    // Clases para RECIBIR la respuesta de la API
    public class ApiResponse
    {
        public int procesoExitoso { get; set; }
        public string mensaje { get; set; }
        public DatosUsuarioApi objeto { get; set; }
    }

    public class DatosUsuarioApi
    {
        public string rpe { get; set; }
        public string correo { get; set; }
        public string nombre { get; set; }
        public string puesto { get; set; }
    }
}