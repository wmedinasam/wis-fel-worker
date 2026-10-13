# Guía del proyecto

- El proyecto usa C# con .NET 10, Worker Service/Generic Host y EF Core 10 con SQL Server; conserva estas tecnologías salvo decisión explícita del usuario.
- La ejecución de cliente se distribuye como contenedor Linux con Docker Compose. No agregues integración nativa con Windows Service/systemd/launchd salvo requerimiento explícito; el SO anfitrión necesita Docker Desktop (Windows/macOS) o Docker Engine (Linux), no el runtime .NET.
- Mantén el alcance de cada cambio acotado. Las conexiones, el esquema FEL, el modelo `documento`, el envío al API, los comandos y el heartbeat siguen pendientes de implementación; consulta el README para el propósito y límites del sistema.
- Nunca agregues secretos al código, a archivos versionados ni a logs: esto incluye credenciales SAE/FEL, credenciales de otras empresas, llaves de certificador y certificados P12. No registres cadenas de conexión completas.
- Mantén el `Dockerfile` multi-stage y no copies archivos locales de secretos a imágenes; verifica que el contenedor pueda detenerse limpiamente y que la configuración de Docker no publique puertos innecesarios.
- Mantén aislada la configuración de cada empresa SAE. No escribas en SAE salvo para la operación autorizada de actualización de clientes cuando `puedeActualizarClientes` esté habilitado.
- Antes de agregar nuevas dependencias o comportamiento, verifica que sean necesarios para el requerimiento. Ejecuta `dotnet build WisFel.Worker.slnx` para validar cambios de código.
