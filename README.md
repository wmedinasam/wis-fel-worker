# WIS FEL Worker

Worker de escritorio para las instalaciones cliente de WIS FEL. Su objetivo es conectar con SQL Server, detectar documentos emitidos por Aspel SAE a través de la cola local de FEL, certificarlos y reflejar el resultado en la base local.

El worker es el **agente** de la plataforma FEL: concentra la lógica de todos los certificadores y todos los países, y es el único componente que habla con ellos. El API de la nube no se comunica con certificadores: gestiona licenciamiento, planes, tenants, empresas, sucursales y agentes, recibe **solo metadatos** del resultado y ordena reenvíos. El contrato que este worker consume está en [`api-architecture.md`](api-architecture.md).

## Responsabilidades previstas

- Leer de `Documents` en la base `FEL` los documentos que el trigger `FEL_EMITIDAS` registra con estado `PENDIENTE`.
- Leer la base SAE correspondiente a cada empresa mediante consultas configurables por empresa, versión de SAE y tipo de documento, y reunir cabecera, emisor, receptor, detalle y adendas para formar un JSON provisional.
- Certificar cada documento con el certificador que corresponda a la empresa y a su país, y actualizar en `FEL.Documents` el estado, UUID, serie y número SAT y el motivo de rechazo.
- Reportar al API el estado de los documentos (solo metadatos) y mantener sincronizado el estado de licencia de cada empresa.
- Ejecutar los comandos que el API entrega en el latido. El contrato define hoy un único comando, `ResendDocument`; probar conexión SAE, reprocesar documentos y actualizar clientes SAE siguen siendo operaciones locales sin comando en el API. La actualización de clientes solo se permite cuando la empresa tenga habilitada la capacidad `puedeActualizarClientes`.
- Reportar el latido cada 30 segundos con la versión del worker y confirmar los comandos ya atendidos.
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

Este repositorio contiene la solución, el proyecto Worker, configuración Docker, el modelo de configuración/licencia de empresas SAE, la base reutilizable para consultar SAE y componer un JSON provisional, el cliente HTTP del API (handshake, refresh, latido y reporte de estados) y el almacén seguro de credenciales y tokens del agente. La conexión de desarrollo a SQL Server se configura mediante User Secrets y, en Development, el worker aplica migraciones al arrancar.

Pendientes: la certificación y los providers por certificador y país, que esperan el contrato de configuración del certificador en el API, y la atención del comando `ResendDocument`, que depende de poder consultar al certificador. El bucle ya corre: sincroniza licencia, consume la cola y late.

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

Los datos SAT, `Observations`, `Request`, `Response` y `Pdf` pueden ser nulos al insertar; se guardarán cuando se produzcan durante el procesamiento. Request y Response aceptan JSON o XML como texto, sin validación del formato por SQL Server. El PDF se guarda en binario dentro de FEL. `Processing` sigue reservado para cuando exista certificador y haya un envío que proteger: el consumo actual de la cola no cambia `Status`.

## Consumo de la cola

`Worker` es un único bucle secuencial, así que ninguna pasada se solapa con otra. En cada tick
(`Worker:PollInterval`, 5 s) hace, en orden: sincronizar licencia si toca, procesar la cola y latir si toca.

| Clave | Valor por defecto | Uso |
| --- | --- | --- |
| `Worker:PollInterval` | `00:00:05` | Cada cuánto se revisa la cola |
| `Worker:BatchSize` | `50` | Documentos por empresa y pasada; máximo 100 por el límite del API |
| `Worker:HeartbeatInterval` | `00:00:30` | Latido |
| `Worker:LicenseSyncInterval` | `00:10:00` | Refresco del estado de licencia |

`DocumentQueueProcessor` hace una pasada por empresa:

1. `IDocumentSendPolicy` decide; una empresa sin licencia se salta completa y no se lee SAE.
2. Toma los documentos con `Status = Pending AND Request IS NULL`, ordenados por `Id`.
3. Arma el JSON con el lector SAE y lo guarda en `Documents.Request` con un solo `SaveChanges`.
4. Reporta los metadatos del lote con estado `Pending`. Si el reporte falla, el JSON ya está guardado y solo
   se pierde ese reporte; el estado definitivo lo contará la certificación.

**No cambia `Status`.** Sin certificador no hay transición que tenga sentido, y dejar filas en `Processing`
sin dueño sería peor: un documento con `Request` guardado es justamente "listo para certificar", y ese mismo
filtro hace la pasada re-entrante y segura ante un reinicio, sin migraciones ni columnas de lease.

Si la lectura SAE de un documento falla, se registra un aviso con su clave natural —sin el texto de la
consulta ni el payload— y la pasada sigue con los demás; ese documento se reintenta en el siguiente ciclo.
Todavía no hay corte de reintentos ni marca `Failed`: hace falta una columna de intentos o de fecha.

El latido envía `workerVersion` y `acknowledgedCommandIds` vacío. Los comandos que lleguen se registran por
`id` y `type` pero **no se confirman**: no se confirma lo que no se atendió, así el API los reentrega cuando
expire el lease en vez de darlos por hechos. Atender `ResendDocument` necesita consultar al certificador.

Si el API no está configurado (`FelApi:BaseUrl` vacía), el worker avisa una vez y queda inactivo: sin
licencia no hay envío posible, y así no se repite el mismo error cada cinco segundos.

## Integración con el API FEL (nube)

El worker consume el API como agente autenticado. La cola local es la fuente de verdad del trabajo pendiente y el API es la autoridad de licenciamiento y el destino de los metadatos.

### Transporte

El API expone HTTPS/REST con JWT; **no ofrece sockets ni WebSocket**, y los comandos se entregan en la respuesta del latido. Por eso el worker no abre sockets crudos: `FelApiClient` (en `src/WisFel.Worker/Api/`) usa un `HttpClient` de `IHttpClientFactory` con `PooledConnectionLifetime` y keep-alive para reutilizar la conexión TLS entre latidos en lugar de renegociarla cada 30 segundos. Si más adelante el API publica un canal persistente (WebSocket o SignalR), el cliente se puede sustituir sin tocar la cola ni los providers; no lo implementes antes de que exista ese contrato.

Resiliencia: `FelApiRetry.NextDelay` aplica backoff exponencial con jitter, respeta `Retry-After` (delta o fecha) en 429 y 503, y no reintenta respuestas no transitorias; el número de intentos se configura con `FelApi:MaxAttempts`. La cola sigue llenándose en `dbo.Documents` mientras no haya red: perder el API no debe detener la captura de documentos.

### Endpoints que consume

| Endpoint | Uso | Límite | Devuelve |
| --- | --- | --- | --- |
| `POST /agent/handshake` | Autenticación inicial con `agentId`, `secret` y `workerVersion` | 10/min por IP | tokens, `licenseState`, `tenantId`, `companyId` |
| `POST /agent/token/refresh` | Rotación del token de agente | 20/min por IP | tokens nuevos y revalidación de licencia |
| `POST /agent/heartbeat` | Latido cada 30 s con `workerVersion` y `acknowledgedCommandIds` | 120/min por agente | `serverTimeUtc` y `pendingCommands` |
| `POST /agent/documents/status` | Reporte de estados en lote | 60/min por agente | `{accepted, rejected[]}` |

No existen otros endpoints para el worker: no inventes rutas de configuración, catálogo ni CRUD.

Manejo de respuestas: el access token dura 15 minutos y el refresh de agente 30 días; ante 401 se refresca y se reintenta una vez. Un 403 significa licencia inválida, no credenciales malas. Cada refresh revoca el anterior y reusar uno ya rotado revoca toda la familia, así que el refresh token vigente debe guardarse en un solo lugar y nunca duplicarse entre procesos ni aparecer en logs.

### Credenciales del agente

Hay **una credencial de agente por empresa SAE**: cada fila de `dbo.SaeCompanies` se asocia a un `agentId` y a los GUID de tenant y empresa del API, de modo que la licencia y los documentos queden separados por empresa igual que en la plataforma. El secreto se muestra una sola vez al crear el agente y vive en User Secrets o variables de entorno, junto a la cadena de conexión; no se versiona, no va en Compose y no se registra en logs.

Hoy el `agentId` y el secreto se leen de la configuración, indexados por el `Id` de la fila en `dbo.SaeCompanies`, que es el `companyKey` que recibe `IFelApiClient`. `FelApi:BaseUrl` debe ser HTTPS, salvo que apunte a `localhost` para probar contra el API de desarrollo:

| Clave | Versionada | Uso |
| --- | --- | --- |
| `FelApi:BaseUrl` | Sí (vacía) | Raíz del API, por ejemplo `https://fel.example.com` |
| `FelApi:WorkerVersion` | Sí | Versión que se reporta en handshake y latido |
| `FelApi:MaxAttempts` / `FelApi:RetryBaseDelay` | Opcional | Intentos (3) y base del backoff (1 s) |
| `FelApi:TokenStorePath` | Sí | Raíz del almacén de tokens; en el contenedor apunta al volumen |
| `FelApi:Agents:<SaeCompanyId>:AgentId` | **No** | GUID del agente de esa empresa |
| `FelApi:Agents:<SaeCompanyId>:Secret` | **No** | Secreto del agente |

```bash
dotnet user-secrets set "FelApi:Agents:1:AgentId" "<guid-del-agente>" --project src/WisFel.Worker/WisFel.Worker.csproj
dotnet user-secrets set "FelApi:Agents:1:Secret" "<secreto-del-agente>" --project src/WisFel.Worker/WisFel.Worker.csproj
```

En Docker se pasan como variables de entorno con `__` como separador: `FelApi__Agents__1__Secret`. La configuración del API se valida en el primer uso del cliente, no al arrancar, para que una instalación sin API configurada siga levantando.

### Almacén seguro de secretos y tokens

`IAgentCredentialStore` es el único punto del worker que toca secretos de agente. Su implementación,
`ProtectedAgentCredentialStore`, separa dos cosas:

- **Entrada**: el `agentId` y el secreto siempre vienen de configuración y nunca se escriben a disco. En
  desarrollo llegan de User Secrets; en Docker, de archivos montados en `/run/secrets`.
- **Refresh token**: se persiste **cifrado** con ASP.NET Core Data Protection en
  `{FelApi:TokenStorePath}/tokens/agent-{empresa}.token`, con permisos `0600`, escritura atómica y el anillo
  de claves en `{FelApi:TokenStorePath}/keys`. Así un reinicio **no** rehace handshake: la primera llamada usa
  `POST /agent/token/refresh`. Si el archivo no se puede descifrar (anillo de claves perdido, archivo de otra
  instalación) se descarta con un aviso y el worker vuelve al handshake; si no se puede escribir, el latido
  sigue funcionando con el token en memoria. Nunca se guarda en FEL ni aparece en logs.

Dentro del contenedor `FelApi:TokenStorePath` es `/var/lib/wis-fel-worker`, un volumen nombrado con permisos
`700` para el usuario no root de la imagen. Sin ese volumen el almacén vive en la capa efímera del contenedor
y cada reinicio rehace handshake: funciona, pero pierde el beneficio.

**No se implementan DPAPI ni Keychain propios.** El artefacto que se distribuye es un contenedor Linux y
ninguna de las dos APIs existe ahí, así que serían código muerto; el almacén seguro del contenedor son los
secretos montados más el cifrado en reposo sobre el volumen. Cuando el proceso sí corre en Windows —un
`dotnet run` de desarrollo— Data Protection cifra su anillo de claves con **DPAPI** (`ProtectKeysWithDpapi`),
sin código criptográfico propio en el repositorio.

Reusar un refresh token ya rotado revoca toda la familia, así que debe existir un **único proceso worker por
instalación**: dos contenedores con la misma credencial de agente se invalidan mutuamente. Si el API rechaza
el refresh con 401, el cliente borra el token guardado y vuelve al handshake.

### Reporte de estados

El lote admite de 1 a 100 documentos y el API hace upsert por `IdempotencyKey`, única por tenant. El worker la deriva de la clave natural de la cola, la misma del índice único que evita duplicados del trigger:

```
{CompanyId}-{LocationId}-{DocumentType}-{InternalId}
```

Los nombres de estado del API no coinciden con los de la cola local. El enum local se conserva y la traducción ocurre solo en el borde del cliente HTTP:

| `DocumentStatus` local | `status` en el API |
| --- | --- |
| `Pending` | `Pending` |
| `Processing` | `Processing` |
| `Signed`, `Delivered` | `Certified` |
| `Rejected` | `Error` |
| `Failed` | `Error`, o `Retrying` si aún quedan reintentos |

`DocumentStatusReportMapper` deriva la clave y traduce el estado; el tipo `DocumentStatusReport` no tiene campos donde quepan `Request`, `Response` ni `Pdf`. La cola local todavía no guarda `attemptCount`, `issuedAtUtc`, `certifiedAtUtc` ni `errorCode`, así que el llamador los pasa al mapper y se envían nulos cuando no hay dato.

Se envían metadatos: `documentType`, `series`, `number` (los datos SAT), `authorizationNumber` con el UUID del certificador, `errorCode` y `errorMessage` derivados del resultado, `attemptCount`, `issuedAtUtc` y `certifiedAtUtc`. **Nunca** se envían `Request`, `Response` ni `Pdf`: el documento firmado no sale del worker y el API solo guarda metadatos, así que los mensajes de error no deben arrastrar el payload.

`branchId` es opcional y es el GUID de una sucursal del API; la cola solo tiene `LocationId` como entero y todavía no hay mapeo acordado, por lo que se envía `null`. Un `branchId` que no pertenezca a la empresa del agente hace que el API rechace ese documento en `rejected[]`.

### Comandos

Los comandos llegan únicamente en el latido, con lease de 30 segundos y un máximo de 20 por respuesta, y se confirman enviando su id en `acknowledgedCommandIds` en el siguiente latido. El cliente los entrega sin interpretarlos: `PendingAgentCommand.Payload` es un `JsonObject` tal como llegó. El único comando definido es `ResendDocument`, con `payload` `{"documentId":"…","idempotencyKey":"…"}`: el worker localiza el documento por `idempotencyKey`, **consulta primero al certificador si ya se certificó** para no duplicar, reenvía si corresponde y reporta el estado resultante.

## Certificadores y países

La lógica de certificación vive completa en el worker: un provider por certificador y país, seleccionado por el certificador asignado a la empresa, su país, el formato que exige (JSON o XML) y el endpoint del ambiente correspondiente (pruebas o producción). El API nunca llama al certificador ni almacena su respuesta firmada.

Falta la pieza de configuración: el API todavía **no expone** un endpoint para que el agente descargue su asignación de certificador ni las credenciales cifradas de la empresa. Hasta que ese contrato exista no hay fuente de configuración de certificador, el worker no certifica y no se deben crear tablas, catálogos locales ni plantillas de certificador por adelantado. Las credenciales de certificador y los certificados P12 no se persisten en FEL, en el repositorio, en la imagen ni en los logs.

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

El nodo `Document` admite además las propiedades dinámicas de cabecera obtenidas desde SAE. El nodo de emisor/receptor y cada item aceptan objetos JSON flexibles porque aún falta acordar los campos y su mapeo. Las claves de `FreeFields` se serializan en orden ordinal para mantener una salida estable; los campos de cabecera no pueden sobrescribir los nodos reservados `Issuer`, `Receiver`, `Items` ni `FreeFields`. El resultado se guarda con `Document.StoreRequestJson()` en `Documents.Request`. El lector SAE construye este borrador para un documento individual y `DocumentQueueProcessor` es quien lo invoca por cada fila encolada; el contrato fiscal final todavía no está definido.

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

El lector solo permite documentos `Pending`, exige que la empresa pertenezca al documento y que la política de licencia permita el envío. No cambia el estado ni llama `SaveChanges`; el componente que lo invoque debe persistir los cambios cuando corresponda. Las consultas configuradas son SQL ejecutable y el Worker no analiza su texto para impedir comandos de escritura. Por ello, la cuenta SQL que el Worker usa para conectarse a las bases SAE debe tener permisos de solo lectura; limita también quién puede modificar las tablas de consulta. El lector abre conexiones SAE para las secciones; no hay administración de plantillas desde una UI.

### Seguridad del payload

El JSON puede incluir datos personales y fiscales. Su destino es el certificador, no el API de la nube: al API solo viajan metadatos. La comunicación es HTTPS con TLS 1.2 o superior; hacia el API la autenticación ya está definida (JWT de agente obtenido en el handshake) y hacia el certificador se usarán las credenciales de la empresa, cuya entrega sigue pendiente. HTTPS protege el tráfico en tránsito, no sustituye autorización del cliente ni controles de acceso, retención y cifrado de backups en el servidor receptor.

No cifrar el JSON manualmente en el worker antes de tener un contrato de claves y rotación: el receptor necesita poder procesarlo. Si el análisis de amenazas exige cifrado adicional extremo a extremo, API y worker deberán acordar cifrado híbrido con clave pública del receptor, gestión/rotación de claves y tratamiento de errores. No incluir credenciales SQL, contraseñas, llaves privadas ni certificados P12 en el JSON. Redactar Request/Response en logs, trazas y métricas, y restringir el acceso y respaldos de FEL, ya que `Documents.Request` se conserva para auditoría/reintento.

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

El API es la autoridad de licenciamiento. El handshake y cada refresh devuelven `licenseState` con uno de tres valores, calculados por el API a partir del plan del tenant y sus días de gracia:

| `licenseState` | Efecto en el worker |
| --- | --- |
| `Active` | Permite certificar y enviar |
| `Grace` | Permite certificar y enviar; la suscripción está por expirar |
| `Suspended` | Deniega el envío |

Ese valor se aplica con `SaeCompany.ApplyLicenseStatusFromCloud(...)`, que persiste el indicador y la fecha de actualización, y es lo que luego evalúa `IDocumentSendPolicy`. El worker no calcula expiraciones ni períodos de gracia por su cuenta.

`AgentLicenseSynchronizer` aplica ese valor: cada `Worker:LicenseSyncInterval` (10 min por defecto) llama `POST /agent/token/refresh` por empresa configurada y guarda el resultado con `ApplyLicenseStatusFromCloud`. Un 403 deniega el envío, un valor desconocido también (deny-by-default), y un fallo de red deja la empresa como estaba: el worker nunca calcula expiraciones ni gracia. Una empresa sin credencial de agente configurada no se sincroniza y por tanto no envía.

Las empresas nuevas quedan sin licencia hasta la primera sincronización y la política deniega la lectura para envío si la empresa no está configurada o no tiene licencia. No habilites manualmente `IsLicensed`. Aún no existe una transición que cambie el estado de `Documents` al denegarse por licencia.

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

La base SAE/FEL debe estar en una instancia SQL Server accesible desde la red del contenedor. No uses `localhost` como servidor SQL desde dentro del contenedor, excepto si configuras explícitamente una ruta hacia el host; usa el DNS o IP alcanzable desde Docker y permite el tráfico SQL Server en la red/firewall.

Los User Secrets del host no se copian al contenedor: en Docker los secretos se montan como archivos y el worker los lee con `AddKeyPerFile("/run/secrets")`. Crea el directorio `secrets/` (ignorado por git y por Docker) con un archivo por clave antes del primer `docker compose up`:

```bash
mkdir -p secrets && chmod 700 secrets
printf '%s' '<cadena-de-conexion>' > secrets/ConnectionStrings__FEL
printf '%s' '<guid-del-agente>'    > secrets/FelApi__Agents__1__AgentId
printf '%s' '<secreto-del-agente>' > secrets/FelApi__Agents__1__Secret
chmod 600 secrets/*
```

| Archivo en `secrets/` | Clave de configuración |
| --- | --- |
| `ConnectionStrings__FEL` | `ConnectionStrings:FEL` |
| `FelApi__Agents__1__AgentId` | `FelApi:Agents:1:AgentId` |
| `FelApi__Agents__1__Secret` | `FelApi:Agents:1:Secret` |

El número del nombre es el `Id` de la fila en `dbo.SaeCompanies`; agrega un archivo y una entrada en `compose.yaml` por cada empresa que atienda la instalación. Los secretos montados tienen prioridad sobre `environment:` y sobre User Secrets, así que nunca hace falta poner una credencial en `compose.yaml`. El volumen `worker-state` guarda el almacén de tokens y debe respaldarse o descartarse como dato sensible.

## Compilar sin Docker

Se requiere el SDK de .NET 10:

```bash
dotnet restore WisFel.Worker.slnx
dotnet build WisFel.Worker.slnx
```

## Seguridad

No incluir en el repositorio ni en la imagen cadenas de conexión, usuarios o contraseñas de SQL Server, llaves de certificador, certificados P12 ni credenciales de empresas que esta instalación no atiende. No pases secretos como argumentos de build, ya que pueden quedar en las capas de la imagen. Usa User Secrets para desarrollo local y define un mecanismo seguro para las instalaciones Docker antes de habilitar su acceso a bases de datos.
