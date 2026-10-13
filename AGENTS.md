# Guía del proyecto

- El proyecto usa C# con .NET 10, Worker Service/Generic Host y EF Core 10 con SQL Server; conserva estas tecnologías salvo decisión explícita del usuario.
- La ejecución de cliente se distribuye como contenedor Linux con Docker Compose. No agregues integración nativa con Windows Service/systemd/launchd salvo requerimiento explícito; el SO anfitrión necesita Docker Desktop (Windows/macOS) o Docker Engine (Linux), no el runtime .NET.
- En Development, `ConnectionStrings:FEL` se obtiene de .NET User Secrets y el arranque aplica migraciones. No pongas cadenas de conexión en `appsettings*.json`, Compose ni otros archivos versionados. Consulta el README para comandos de configuración y migración.
- Cada fila `SaeCompany` representa una base SAE; deriva su nombre desde la versión y número de empresa, sin persistir credenciales por empresa. El servidor/credenciales se comparten desde secretos externos a FEL.
- La autorización de envío es deny-by-default: solo una empresa configurada con licencia confirmada puede autorizar envío. La nube será fuente autoritativa; no inventes expiración/períodos de gracia ni marques documentos hasta que exista el modelo `Documents` y el contrato API.
- Mantén el alcance de cada cambio acotado. El modelo de documentos, envío al API, sincronización cloud de licencias, comandos y heartbeat siguen pendientes; consulta el README para el propósito y límites del sistema.
- Nunca agregues secretos al código, a archivos versionados ni a logs: esto incluye credenciales SAE/FEL, credenciales de otras empresas, llaves de certificador y certificados P12. No registres cadenas de conexión completas.
- Mantén el `Dockerfile` multi-stage y no copies archivos locales de secretos a imágenes; verifica que el contenedor pueda detenerse limpiamente y que la configuración de Docker no publique puertos innecesarios.
- Mantén aislada la configuración de cada empresa SAE. No escribas en SAE salvo para la operación autorizada de actualización de clientes cuando `puedeActualizarClientes` esté habilitado.
- Antes de agregar nuevas dependencias o comportamiento, verifica que sean necesarios para el requerimiento. Ejecuta `dotnet build WisFel.Worker.slnx` para validar cambios de código.
