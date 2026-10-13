# WIS FEL Worker

Worker de escritorio para las instalaciones cliente de WIS FEL. Su objetivo es conectar con SQL Server, detectar documentos emitidos por Aspel SAE a través de la cola local de FEL, enviarlos al API de certificación cuando esté disponible y reflejar el resultado en la base local.

## Responsabilidades previstas

- Leer de `Documents` en la base `FEL` los documentos que el trigger `FEL_EMITIDAS` registra con estado `PENDIENTE`.
- Leer la base SAE correspondiente a cada empresa mediante consultas configurables por empresa, versión de SAE y tipo de documento, y reunir cabecera, emisor, receptor, detalle y adendas para formar un JSON provisional.
- Enviar documentos al API, aún pendiente de desarrollo, y actualizar en `FEL.Documents` el estado, UUID, serie y número SAT y el motivo de rechazo.
- Ejecutar comandos remotos autorizados: probar conexión SAE, reprocesar documentos y actualizar clientes SAE. La actualización de clientes solo se permite cuando la empresa tenga habilitada la capacidad `puedeActualizarClientes`.
- Reportar el latido del worker cada 30 segundos.
- Conservar XML y PDF en la tabla `Documents` cuando esa opción esté configurada.

Cada empresa SAE tiene una base de datos independiente. El cliente comparte el servidor SQL y las credenciales entre FEL y todas las empresas SAE; el worker solo debe atender empresas configuradas para esa instalación. No debe escribir en SAE salvo al ejecutar el comando autorizado de actualización de clientes.

## Tecnologías

- **C# y .NET 10**.
- **Worker Service con Generic Host** para el proceso en segundo plano.
- **Entity Framework Core 10 y proveedor SQL Server** para el esquema de FEL; **Microsoft.Data.SqlClient** para ejecutar las consultas SAE configuradas.
- **SQL Server** como motor de las bases SAE y FEL.
- **Docker** para distribuir y ejecutar una imagen Linux en PCs Windows, Linux y macOS sin instalar el runtime .NET 10 en el host.
- **EF Core migrations** para crear la base `FEL` si todavía no existe y evolucionar su esquema.

## Estado de este repositorio

Este repositorio contiene la solución, el proyecto Worker, configuración Docker, el modelo de configuración/licencia de empresas SAE y la base reutilizable para consultar SAE y componer un JSON provisional. La conexión de desarrollo a SQL Server se configura mediante User Secrets y, en Development, el worker aplica migraciones al arrancar. El consumo automático de la cola, el envío al API, comandos, heartbeat y sincronización de licencias con la nube todavía están pendientes.

Las migraciones crean `FEL` si hace falta, mantienen `SaeCompanies`, `SaeCommonQueries`, `SaeDocumentQueries` y `dbo.Documents` para la cola. Todavía no se incluyen tablas de detalle, stored procedures ni el trigger de SAE.

## Cola local de documentos

El trigger `FEL_EMITIDAS` de cada base SAE debe insertar en `FEL.dbo.Documents` al emitir un documento. Debe proporcionar `CompanyId`, `LocationId`, `InternalNumber`, `InternalSerial`, `InternalId` y `DocumentType`; si omite `Status`, SQL asigna `Pending`. `CompanyId` debe existir en `dbo.SaeCompanies`. Esta migración solo modifica FEL: no instala ni cambia triggers en las bases SAE.

La tabla también tiene un `Id` identity para identificar cada fila. El índice único `(CompanyId, LocationId, DocumentType, InternalId)` evita que el trigger encole dos veces el mismo documento. `LocationId` e `InternalId` son enteros; número y serial interno son texto para conservar su formato. `DocumentType` acepta códigos como `FCAM`, `FACT`, `NCRE` y `NDEB`.

| Columna | Tipo SQL | Uso |
| --- | --- | --- |
| `CompanyId` | `int` | FK a `SaeCompanies.Id` |
| `LocationId` | `int` | Ubicación SAE |
| `Status` | `nvarchar(20)` | `Pending`, `Processing`, `Signed`, `Delivered`, `Rejected` o `Failed` |
| `InternalNumber` | `nvarchar(50)` | Número interno del documento SAE |
| `InternalSerial` | `nvarchar(20)` | Serie interna SAE |
| `InternalId` | `int` | Identificador interno SAE |
| `DocumentType` | `nvarchar(10)` | Tipo de comprobante SAE |
| `UUID` | `nvarchar(36) NULL` | UUID devuelto al firmar |
| `Serial` | `nvarchar(20) NULL` | Serie SAT |
| `Number` | `nvarchar(20) NULL` | Número SAT |
| `Observations` | `nvarchar(max) NULL` | Problemas de envío o motivo de rechazo |
| `Request` | `nvarchar(max) NULL` | JSON o XML Request enviado |
| `Response` | `nvarchar(max) NULL` | JSON o XML Response formado/recibido |
| `Pdf` | `varbinary(max) NULL` | Archivo PDF |

Los datos SAT, `Observations`, `Request`, `Response` y `Pdf` pueden ser nulos al insertar; se guardarán cuando se produzcan durante el procesamiento. Request y Response aceptan JSON o XML como texto, sin validación del formato por SQL Server. El PDF se guarda en binario dentro de FEL. `Processing` está disponible para que una futura implementación reserve el documento durante su envío. El Worker aún no consume esta cola ni llama al API, que sigue pendiente.

## Borrador del JSON Request

`DocumentRequestJsonBuilder` serializa el DTO provisional en `src/WisFel.Worker/Payloads`. La estructura raíz es `Document`, que contiene los nodos `Issuer`, `Receiver`, el arreglo `Items` y el objeto clave/valor `FreeFields` para adendas:

```json
{
  "Document": {
    "Issuer": {},
    "Receiver": {},
    "Items": [],
    "FreeFields": {}
  }
}
```

El nodo `Document` admite además las propiedades dinámicas de cabecera obtenidas desde SAE. El nodo de emisor/receptor y cada item aceptan objetos JSON flexibles porque aún falta acordar los campos y su mapeo. Las claves de `FreeFields` se serializan en orden ordinal para mantener una salida estable; los campos de cabecera no pueden sobrescribir los nodos reservados `Issuer`, `Receiver`, `Items` ni `FreeFields`. El resultado se puede guardar con `Document.StoreRequestJson()` en `Documents.Request`. El lector SAE construye este borrador para un documento individual; todavía no consume automáticamente la cola ni envía HTTP y el contrato fiscal final no está definido.

## Consultas SAE configurables

Las consultas comunes y las consultas específicas de documento tienen alcances distintos, de modo que un ajuste a cliente o catálogo de productos se comparte entre tipos de comprobante sin impedir que las partidas cambien con su tabla de origen:

| Tabla FEL | Alcance | Consultas |
| --- | --- | --- |
| `dbo.SaeCommonQueries` | Una fila por empresa y versión SAE; compartida por todos sus tipos documentales | `CustomerSql` y `ProductSql` |
| `dbo.SaeDocumentQueries` | Una fila por empresa, versión SAE y tipo de documento | `DocumentSql`, `IssuerSql`, `ItemsSql` y `FreeFieldsSql` |

La migración crea la estructura, pero no carga plantillas genéricas: se deben configurar las sentencias para el esquema real del cliente en SQL Server. No se crean procedimientos por tipo o empresa y no se ejecuta DDL ni se modifican las bases SAE.

Las consultas comunes son:

| Columna | Resultado esperado |
| --- | --- |
| `CustomerSql` | Una fila y una columna `Json` con el objeto del cliente que se coloca en `Document.Receiver`. |
| `ProductSql` | Una fila y una columna `Json` con los datos del producto buscado en `INVE{suffix}`. Se invoca una vez por código de producto distinto presente en el documento con su `@ProductCode`, y el resultado se agrega a `Items[].Product`. |

Las consultas específicas por documento son:

| Columna | Resultado esperado |
| --- | --- |
| `DocumentSql` | Una fila y una columna `Json` con objeto JSON; sus propiedades se agregan a `Document`. |
| `IssuerSql` | Una fila y una columna `Json` con objeto JSON para `Issuer`. |
| `ItemsSql` | Cero o más filas, cada una con una columna `Json` para una partida. Cada objeto debe incluir `ProductCode` como texto no vacío; las partidas pueden venir de `PAR_FACTF{suffix}`, `PAR_FACTD{suffix}` u otra tabla según el tipo. |
| `FreeFieldsSql` | Cero o más filas con las columnas `Name` y `Value`; `Name` debe ser no nulo, no vacío y único. `Value` puede ser nulo. |

Las consultas comunes de cliente y las consultas documentales reciben estos parámetros SQL tipados, que deben referenciarse como parámetros y no interpolarse como texto:

| Parámetro | Tipo | Origen |
| --- | --- | --- |
| `@InternalId` | `int` | Identificador interno SAE del documento |
| `@InternalNumber` | `nvarchar(50)` | Número interno del documento |
| `@InternalSerial` | `nvarchar(20)` | Serie interna |
| `@LocationId` | `int` | Ubicación SAE |
| `@DocumentType` | `nvarchar(10)` | Código del tipo en mayúsculas |

`ProductSql` recibe además `@ProductCode` (`nvarchar(50)`) con el código emitido por `ItemsSql`. La tabla de inventario debe derivarse de la base SAE de la empresa, típicamente `INVE{suffix}`; las partidas sí son configurables por tipo documental, por ejemplo `PAR_FACTF{suffix}` en facturas y `PAR_FACTD{suffix}` en notas de crédito. El SQL configurable debe devolver `ProductCode` en cada resultado de partida y un objeto JSON de producto por código.

Las consultas pueden usar `FOR JSON PATH, WITHOUT_ARRAY_WRAPPER` para producir objetos y conservar libertad para seleccionar, concatenar o transformar campos. Los nombres reales de columnas y relaciones dependen de la versión y personalización SAE; los nombres de tablas mencionados son orientativos, no plantillas listas para producción.

Al pasar a las consultas comunes, la migración traslada `ReceiverSql` a `CustomerSql` solo cuando el SQL coincide entre todos los tipos de una empresa/versión. Crea esa configuración común como deshabilitada y con `ProductSql` vacío para exigir que un DBA configure el producto desde `INVE` y habilite expresamente la fila. Si había consultas de cliente distintas por tipo, la migración se detiene sin eliminar datos: consolida primero `ReceiverSql` para esos tipos y vuelve a aplicar la migración. No habilites una configuración común hasta completar ambas consultas.

El lector solo permite documentos `Pending`, exige que la empresa pertenezca al documento y que la política de licencia permita el envío. No cambia el estado ni llama `SaveChanges`; el componente que lo invoque debe persistir los cambios cuando corresponda. Las consultas configuradas son SQL ejecutable y el Worker no analiza su texto para impedir comandos de escritura. Por ello, la cuenta SQL que el Worker usa para conectarse a las bases SAE debe tener permisos de solo lectura; limita también quién puede modificar las tablas de consulta. El lector abre conexiones SAE para las secciones; no hay todavía consumo continuo de la cola ni administración de plantillas desde una UI.

### Seguridad del payload

El JSON puede incluir datos personales y fiscales. La recomendación para la futura comunicación es HTTPS con TLS 1.2 o superior y autenticación servicio-a-servicio por acordar al diseñar el API. HTTPS protege el tráfico en tránsito, no sustituye autorización del cliente ni controles de acceso, retención y cifrado de backups en el servidor receptor.

No cifrar el JSON manualmente en el worker antes de tener un contrato de claves y rotación: el API necesita poder procesarlo. Si el análisis de amenazas exige cifrado adicional extremo a extremo, API y worker deberán acordar cifrado híbrido con clave pública del receptor, gestión/rotación de claves y tratamiento de errores. No incluir credenciales SQL, contraseñas, llaves privadas ni certificados P12 en el JSON. Redactar Request/Response en logs, trazas y métricas, y restringir el acceso y respaldos de FEL, ya que `Documents.Request` se conserva para auditoría/reintento.

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

La nube será la autoridad de licenciamiento. FEL conserva por empresa el indicador recibido y la fecha de actualización. Hasta que se implemente la sincronización con el API, las empresas nuevas quedan sin licencia y la política `IDocumentSendPolicy` deniega la lectura para envío si la empresa no está configurada o no tiene licencia. No habilites manualmente `IsLicensed`. Aún no existe una transición que cambie el estado de `Documents` al denegarse por licencia.

## Desarrollo local con SQL Server

El entorno de Development usa SQL Server en `localhost:1433`, base `FEL` y la clave de configuración `ConnectionStrings:FEL`. La cadena de conexión está guardada en el almacén local de **.NET User Secrets**, no en el repositorio ni en Docker. Para configurarla en otra máquina, establece el valor localmente sin versionarlo:

```bash
dotnet user-secrets set "ConnectionStrings:FEL" "<cadena-de-conexion-local>" --project src/WisFel.Worker/WisFel.Worker.csproj
```

El servidor local debe aceptar conexiones TCP en el puerto 1433 y el login configurado debe tener permiso para crear la base y aplicar migraciones.

El proyecto de desarrollo aplica las migraciones automáticamente al iniciar en `Development`. Las migraciones de empresas, documentos y consultas compartidas SAE se añaden a FEL al iniciar después de actualizar el código.

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
