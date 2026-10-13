# WIS FEL Worker

Worker de escritorio para las instalaciones cliente de WIS FEL. Su objetivo es conectar con SQL Server, detectar documentos emitidos por Aspel SAE a través de la cola local de FEL, enviarlos al API de certificación cuando esté disponible y reflejar el resultado en la base local.

## Responsabilidades previstas

- Leer de `Documents` en la base `FEL` los documentos que el trigger `FEL_EMITIDAS` registra con estado `PENDIENTE`.
- Consultar en la base SAE correspondiente a cada empresa las tablas con sufijo de empresa (por ejemplo, `FACTF01`, `FACTF02`) y reunir cabecera, emisor, receptor, detalle, impuestos y adendas para formar el modelo `documento`.
- Enviar documentos al API, aún pendiente de desarrollo, y actualizar en `FEL.Documents` el estado, UUID, serie y número SAT y el motivo de rechazo.
- Ejecutar comandos remotos autorizados: probar conexión SAE, reprocesar documentos y actualizar clientes SAE. La actualización de clientes solo se permite cuando la empresa tenga habilitada la capacidad `puedeActualizarClientes`.
- Reportar el latido del worker cada 30 segundos.
- Conservar XML y PDF en la tabla `Documents` cuando esa opción esté configurada.

Cada empresa SAE tiene una base de datos independiente. El cliente comparte el servidor SQL y las credenciales entre FEL y todas las empresas SAE; el worker solo debe atender empresas configuradas para esa instalación. No debe escribir en SAE salvo al ejecutar el comando autorizado de actualización de clientes.

## Tecnologías

- **C# y .NET 10**.
- **Worker Service con Generic Host** para el proceso en segundo plano.
- **Entity Framework Core 10 y proveedor SQL Server** para FEL y, más adelante, SAE.
- **SQL Server** como motor de las bases SAE y FEL.
- **Docker** para distribuir y ejecutar una imagen Linux en PCs Windows, Linux y macOS sin instalar el runtime .NET 10 en el host.
- **EF Core migrations** para crear la base `FEL` si todavía no existe y evolucionar su esquema.

## Estado de este repositorio

Este repositorio contiene la solución, el proyecto Worker, configuración Docker y el modelo de configuración/licencia de empresas SAE. La conexión de desarrollo a SQL Server se configura mediante User Secrets y, en Development, el worker aplica migraciones al arrancar. La lógica de documentos, extracción/envío, comandos, heartbeat, sincronización de licencias con la nube y el API todavía están pendientes.

La migración inicial crea `FEL` y el historial de EF. La migración de empresas agrega `SaeCompanies`, con la versión SAE, el número de empresa y el estado local de licencia; no crea todavía tablas de documentos ni stored procedures. Las instalaciones existentes aplican las migraciones pendientes sin recrear la base.

## Empresas SAE y licenciamiento

Cada empresa se configura en FEL con una versión SAE y un número de empresa del 1 al 99. La base se deriva como `SAE{códigoDeVersión}Empre{número:00}`. La versión mínima es 4.0 y admite un decimal:

| Versión configurada | Número de empresa | Base derivada |
| --- | ---: | --- |
| 4.0 | 1 | `SAE40Empre01` |
| 4.6 | 2 | `SAE46Empre02` |
| 5.0 | 99 | `SAE50Empre99` |

El servidor y las credenciales se comparten con la conexión de FEL y no se guardan en las filas de empresa. `SaeConnectionStringFactory` conserva el servidor y las credenciales de la conexión compartida y sustituye únicamente la base de datos.

Para registrar una empresa, inserta su versión y número en `dbo.SaeCompanies`; el estado de licencia inicia denegado:

```sql
INSERT INTO dbo.SaeCompanies (SaeVersion, CompanyNumber)
VALUES (4.6, 1);
```

La nube será la autoridad de licenciamiento. FEL conserva por empresa el indicador recibido y la fecha de actualización. Hasta que se implemente la sincronización con el API, las empresas nuevas quedan sin licencia y la política `IDocumentSendPolicy` deniega el envío si la empresa no está configurada o no tiene licencia. No habilites manualmente `IsLicensed`. El futuro flujo de documentos deberá reflejar la denegación por falta de licencia como rechazo local; aún no se marca `Documents` porque esa tabla y sus estados no forman parte del modelo actual.

## Desarrollo local con SQL Server

El entorno de Development usa SQL Server en `localhost:1433`, base `FEL` y la clave de configuración `ConnectionStrings:FEL`. La cadena de conexión está guardada en el almacén local de **.NET User Secrets**, no en el repositorio ni en Docker. Para configurarla en otra máquina, establece el valor localmente sin versionarlo:

```bash
dotnet user-secrets set "ConnectionStrings:FEL" "<cadena-de-conexion-local>" --project src/WisFel.Worker/WisFel.Worker.csproj
```

El servidor local debe aceptar conexiones TCP en el puerto 1433 y el login configurado debe tener permiso para crear la base y aplicar migraciones.

El proyecto de desarrollo aplica las migraciones automáticamente al iniciar en `Development`. La migración de `SaeCompanies` se añade a FEL al iniciar después de actualizar el código.

Al ejecutar con el perfil Development, el worker llama `Database.MigrateAsync()` durante el arranque. EF Core crea `FEL` si no existe y aplica las migraciones pendientes. También se puede aplicar manualmente:

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/WisFel.Worker/WisFel.Worker.csproj --startup-project src/WisFel.Worker/WisFel.Worker.csproj
```

Para generar una migración después de modificar el modelo:

```bash
dotnet tool run dotnet-ef migrations add NombreDeLaMigracion --project src/WisFel.Worker/WisFel.Worker.csproj --startup-project src/WisFel.Worker/WisFel.Worker.csproj
```

Configura `DOTNET_ENVIRONMENT=Development` al ejecutar comandos EF manuales para que .NET cargue User Secrets. En Linux/macOS:

```bash
export DOTNET_ENVIRONMENT=Development
```

En PowerShell:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
```

`dotnet run` con el perfil incluido ya establece ese entorno.

## Ejecutar con Docker

El host no requiere el runtime ni el SDK de .NET 10, pero sí Docker:

- **Windows y macOS:** Docker Desktop configurado para contenedores Linux.
- **Linux:** Docker Engine y Docker Compose plugin.

Desde la raíz del repositorio:

```bash
docker compose up --build -d
docker compose logs -f wis-fel-worker
```

Para detener el worker:

```bash
docker compose down
```

Compose compila una imagen para la arquitectura de la máquina local. Para distribuir imágenes a máquinas con arquitecturas diferentes, se deben publicar las arquitecturas destino (por ejemplo, `linux/amd64` y `linux/arm64`) con Docker Buildx.

La base SAE/FEL debe estar en una instancia SQL Server accesible desde la red del contenedor. No uses `localhost` como servidor SQL desde dentro del contenedor, excepto si configuras explícitamente una ruta hacia el host; usa el DNS o IP alcanzable desde Docker y permite el tráfico SQL Server en la red/firewall. Los User Secrets del host no se copian al contenedor; falta definir la configuración segura de SQL para instalaciones Docker.

## Compilar sin Docker

Se requiere el SDK de .NET 10:

```bash
dotnet restore WisFel.Worker.slnx
dotnet build WisFel.Worker.slnx
```

## Seguridad

No incluir en el repositorio ni en la imagen cadenas de conexión, usuarios o contraseñas de SQL Server, llaves de certificador, certificados P12 ni credenciales de empresas que esta instalación no atiende. No pases secretos como argumentos de build, ya que pueden quedar en las capas de la imagen. Usa User Secrets para desarrollo local y define un mecanismo seguro para las instalaciones Docker antes de habilitar su acceso a bases de datos.
