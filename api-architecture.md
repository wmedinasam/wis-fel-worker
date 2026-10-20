# FEL API: Plataforma de Firma Electrónica

> Espejo de [`wis-fel-api/README.md`](../wis-fel-api/README.md). Si cambia el contrato, se actualizan ambos en el mismo cambio.

API multi-tenant para la plataforma de facturación electrónica (FEL). Gestiona el licenciamiento, los planes, los clientes (tenants), las empresas, las sucursales y los **agentes** (workers locales).

**La API no se comunica con los certificadores.** Los workers, que viven en otro repositorio, certifican los documentos y reportan aquí solo su estado. Desde la API se consulta ese estado y se ordena un reenvío manual, que ejecuta el worker.

> Estado: en desarrollo. Parte del diseño está descrito en [`AGENTS.md`](AGENTS.md) y todavía no está implementada. Ver [Estado actual y limitaciones](#estado-actual-y-limitaciones).

---

## Tabla de contenidos

- [Tecnologías](#tecnologías)
- [Arquitectura](#arquitectura)
- [Modelo de dominio](#modelo-de-dominio)
- [Autenticación y autorización](#autenticación-y-autorización)
- [Requisitos previos](#requisitos-previos)
- [Inicio rápido](#inicio-rápido)
- [Probar en Scalar](#probar-en-scalar)
- [Endpoints y ejemplos](#endpoints-y-ejemplos)
- [Configuración](#configuración)
- [Migraciones de base de datos](#migraciones-de-base-de-datos)
- [Tests](#tests)
- [Estado actual y limitaciones](#estado-actual-y-limitaciones)
- [Contribuir](#contribuir)

---

## Tecnologías

| Componente | Tecnología / versión |
|---|---|
| Framework | .NET 10 (`net10.0`), ASP.NET Core Web API con controllers |
| ORM | Entity Framework Core 10.0.12 |
| Base de datos | SQL Server (`Microsoft.EntityFrameworkCore.SqlServer`) |
| Autenticación | `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 (HS256) |
| Hash de contraseñas | `PasswordHasher<UserAccount>` de `Microsoft.AspNetCore.Identity` |
| Documentación API | `Microsoft.AspNetCore.OpenApi` 10.0.12 + `Scalar.AspNetCore` 2.18.0 |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` (ventanas fijas) |
| Tests | xUnit 2.9.3, `Microsoft.AspNetCore.Mvc.Testing` 10.0.12, EF Core InMemory |

Gestión central de versiones de paquetes con `wis-fel-api/Directory.Packages.props`.

---

## Arquitectura

### Estructura de la solución

```
wis-fel-api/                      Raíz del repositorio
├── README.md
├── AGENTS.md                     Plan de diseño de la plataforma
├── .config/dotnet-tools.json     dotnet-ef 10.0.12 (herramienta local)
└── wis-fel-api/                  Solución
    ├── wis-fel-api.slnx
    ├── Directory.Packages.props  Versiones centrales de paquetes
    ├── fel-api/                  Web API (namespace fel_api)
    │   ├── Program.cs            DI, autenticación, rate limiting, pipeline y modo bootstrap
    │   ├── Controllers/          AuthController, AgentController, PlatformAgentsController,
    │   │                         PlatformCertifierCredentialsController,
    │   │                         PlatformDocumentsController, TenantDocumentsController
    │   ├── Auth/                 Emisión de JWT, opciones JWT, hashing de tokens
    │   ├── RateLimiting/         Opciones de rate limiting
    │   ├── Services/
    │   │   ├── Auth/             Login, refresh tokens, perfil cliente, bootstrap admin
    │   │   ├── Agents/           Provisionamiento, handshake, heartbeat, licencia
    │   │   ├── Catalog/          Configuración de certificador por empresa y cifrado de credenciales
    │   │   ├── Documents/        Estado reportado por el worker, consulta y reenvío
    │   │   └── Subscriptions/    Validación de cuotas del plan
    │   ├── Domain/               Entidades y reglas (Agents, Audit, Catalog, Documents,
    │   │                         Identity, Subscriptions, Tenants)
    │   ├── Data/                 FelApiDbContext y migraciones
    │   └── Properties/launchSettings.json
    └── fel-api.Tests/            Tests xUnit
```

### Flujo por capas

```
 HTTP ──► Controllers ──► Services ──► Domain / EF Core ──► SQL Server
           (validación    (reglas de     (entidades,
            de entrada,    negocio,       DbContext)
            políticas)     licencia,
                           cuotas,
                           tokens)
```

### Flujo de un documento

```
Certificador          Worker (otro repo)                 FEL API                     Portal
     │◄── certifica ──────│                                  │                          │
     │─── resultado ─────►│                                  │                          │
     │                    │ POST /agent/documents/status     │                          │
     │                    │ (lote: estado, autorización,     │                          │
     │                    │  error, intentos)                │                          │
     │                    │─────────────────────────────────►│ upsert por IdempotencyKey│
     │                    │                                  │◄── GET .../documents ────│
     │                    │                                  │◄── POST .../resend ──────│
     │                    │                                  │ crea PendingAgentCommand │
     │                    │ POST /agent/heartbeat            │ "ResendDocument"         │
     │                    │─────────────────────────────────►│                          │
     │                    │◄── pendingCommands ──────────────│                          │
     │◄── reenvía ────────│ (consulta antes si ya se         │                          │
     │                    │  certificó) y reporta el estado  │                          │
```

### ¿Por qué el reenvío lo ejecuta el worker y no la API?

- El worker tiene el documento original, las credenciales descifradas del certificador, los providers por país y la cola con reintentos. La API no tiene nada de eso, y guardarlo aquí implicaría centralizar datos fiscales y credenciales.
- Antes de reintentar hay que consultar al certificador si el documento ya se certificó, para evitar duplicados. Solo el worker puede hacerlo.
- La API solo **ordena** el reenvío. Lo hace con un comando que el worker recibe en el heartbeat, la misma infraestructura de comandos con lease que ya existe.

---

## Modelo de dominio

Los datos se aíslan por tenant en la capa de aplicación, usando las columnas `tenant_id` y `company_id` y los claims del token. No hay Row Level Security: la base es SQL Server.

- **Tenant**: cliente de la plataforma. Tiene empresas, usuarios, roles y una suscripción.
- **Company**: empresa del tenant con `CountryCode`, `TaxId`, `CurrencyCode` y `Timezone`. El nombre es único por tenant.
- **Branch**: sucursal de una empresa. El nombre es único por empresa.
- **Identidad**:
  - `UserAccount`, `ExternalUserIdentity` (issuer + subject de un IdP externo), `PlatformAdministrator`.
  - `TenantMembership`, `TenantRole`, `TenantUserRole`, `TenantRoleCompany`: roles por tenant, opcionalmente limitados a ciertas empresas.
  - `ActivationCode`: modelado pero sin uso todavía.
- **Suscripciones**:
  - `SubscriptionPlan` define `BillingCycle` (Monthly, Annual, Lifetime), límites (`MaxCompanies`, `MaxBranches`, `MaxUsers`, `MaxAgents`), `GracePeriodDays` y `AllowedCountries`.
  - `TenantSubscription` tiene `ExpiresAtUtc` e `IsCurrent`. Hay un índice único filtrado por `TenantId`.
  - Estados calculados por `SubscriptionStateCalculator`:
    - `Active`: antes de `ExpiresAtUtc - GracePeriodDays`.
    - `Grace`: desde ese punto hasta la expiración.
    - `Suspended`: en o después de la expiración.
- **Agentes**:
  - `Agent` tiene `CredentialHash`, datos de la máquina, `LastHandshakeAtUtc`, `LastHeartbeatAtUtc` y `WorkerVersion`.
  - `PendingAgentCommand` guarda comandos con lease (`LeaseExpiresAtUtc`) y `AcknowledgedAtUtc`. Hoy se usa para `ResendDocument`.
  - `RefreshToken` guarda solo el hash del token, con `FamilyId` y `ReplacedByTokenHash`.
- **Catálogo de certificadores**: es configuración que consume el worker. La API no llama a estos endpoints.
  - `Certifier` (por ejemplo `INFILE`) y `CertifierCountryConfiguration` (país, `EndpointUrl` HTTPS, formato Json o Xml).
  - `CompanyCertifierAssignment`: una asignación por empresa, con `CredentialsEncrypted` y `UseProduction`. Las credenciales se cifran con ASP.NET Core Data Protection (`CertifierCredentialProtector`) antes de guardarse y solo se descifran al servirlas al worker por TLS.
- **Documentos** (`Document`): guarda **solo metadatos** reportados por el worker. El XML o JSON firmado nunca sale del worker.
  - Campos: `IdempotencyKey` (única por tenant), `DocumentType`, `Series`, `Number`, `Status`, `AuthorizationNumber` (UUID del certificador), `ErrorCode`, `ErrorMessage`, `AttemptCount`, `IssuedAtUtc`, `CertifiedAtUtc`, `LastReportedAtUtc` y `ResendRequestedAtUtc`. También los ids de agente, empresa y sucursal.
  - Estados (`DocumentStatus`, iguales al outbox del worker): `Pending`, `Processing`, `Certified`, `Error`, `Retrying`.
- **AuditLog**: solo se escribe al actualizar credenciales de certificador (`certifier-credentials-updated`). El resto de eventos todavía no se auditan.

---

## Autenticación y autorización

Hay dos esquemas JWT:

| Esquema | Uso | Emisor / audiencia |
|---|---|---|
| `LocalJwt` | Tokens emitidos por esta API (HS256) | `Jwt:Issuer` / `PlatformAudience` o `AgentAudience` |
| `ClientJwt` | IdP externo: `GET /auth/me` y `/tenant/{tenantId}/documents` | `ClientJwt:Authority` / `ClientJwt:Audience` |

Políticas:

- **`PlatformAdministrator`**: `LocalJwt` con `principal_type=platform_admin`.
- **`Agent`**: `LocalJwt` con `principal_type=agent` y los claims `agent_id`, `tenant_id`, `company_id`.

Para agentes y administradores, el tenant y la empresa siempre salen del token, nunca del cuerpo de la petición.

Los usuarios de tenant usan el token del IdP, que no trae tenant. Por eso el `tenantId` va en la ruta y **solo se acepta si el usuario tiene una membresía activa** en ese tenant.
- Las empresas visibles salen de sus roles: `TenantRoleCompanies`.
- Un rol sin empresas asignadas da acceso a todas las empresas del tenant.

### Vida de los tokens

- Access token: 15 minutos (plataforma y agente).
- Refresh token: 7 días (plataforma) y 30 días (agente).
- Cada refresh revoca el token anterior. Si se reutiliza un refresh token ya rotado, se revoca toda la familia.
- Un token de plataforma no sirve en endpoints de agente, y viceversa.
- Al refrescar un agente se vuelve a validar la licencia del tenant.

### Secretos

- Los secretos de agente son 32 bytes aleatorios en Base64Url. Se guarda el hash SHA-256 (`Auth/TokenHashing.cs`) y la comparación es de tiempo constante.
- El secreto se muestra una sola vez, al crear el agente.

### Licencia

`AgentLicenseValidator` exige una suscripción `IsCurrent` con plan activo y estado distinto de `Suspended`. Si no se cumple, los endpoints de agente responden 403.

### Rate limiting

Ventanas fijas, cola 0, respuesta 429 con `Retry-After`. Particiones por IP (login y refresh de plataforma, handshake y refresh de agente), por `sub` (`/auth/me` y documentos de tenant), por `agent_id` (heartbeat, estado de documentos y configuración del certificador) y por admin (gestión de agentes y documentos de plataforma).

---

## Requisitos previos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server (local, Docker o remoto)
- `openssl` para generar la clave de firma
- Opcional: `dotnet-ef` (se instala con `dotnet tool restore`)

---

## Inicio rápido

```bash
# 1. Clonar el repositorio
git clone <url-del-repositorio> wis-fel-api
cd wis-fel-api

# 2. Restaurar herramientas locales (dotnet-ef)
dotnet tool restore

# 3. Configurar secretos (no van en appsettings.json)
dotnet user-secrets set "ConnectionStrings:FelApi" \
  "Server=localhost;Database=FelApi;User Id=sa;Password=<tu-password>;TrustServerCertificate=True" \
  --project wis-fel-api/fel-api

dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 64)" \
  --project wis-fel-api/fel-api

# 4. Crear la base de datos y aplicar migraciones
dotnet ef database update --project wis-fel-api/fel-api

# 5. Crear el primer administrador de plataforma (pide la contraseña sin eco)
dotnet run --project wis-fel-api/fel-api -- --bootstrap-platform-admin admin@ejemplo.com

# 6. Levantar la API (en Development carga datos de prueba la primera vez)
dotnet run --project wis-fel-api/fel-api
```

Notas:

- Datos de prueba (`Data/DevelopmentDataSeeder.cs`): se cargan en Development cuando no hay tenants. Crean un tenant con empresas GT y SV, sucursales, agentes, documentos en distintos estados y un usuario restringido a SV. La consola muestra el admin (`admin@fel.local`) y el secreto del agente. Se desactivan con `DevelopmentSeed:Enabled=false`.

- `Jwt:SigningKey` debe ser base64 de al menos 32 bytes. Si falta, la aplicación no arranca.
- El bootstrap requiere una contraseña de al menos 12 caracteres y solo funciona si no existe ningún administrador de plataforma. Termina sin levantar el servidor web.
- Las variables de entorno sirven igual que los user-secrets, usando `__` como separador: `ConnectionStrings__FelApi`, `Jwt__SigningKey`.

Con el perfil `http` la API queda en `http://localhost:5070`. El perfil `https` usa `https://localhost:7168`.

---

## Probar en Scalar

Scalar solo está disponible en el entorno `Development`, que es el valor de `launchSettings.json`.

1. Levanta la API con `dotnet run --project wis-fel-api/fel-api`.
2. Abre `http://localhost:5070/scalar/v1`. El documento OpenAPI está en `http://localhost:5070/openapi/v1.json`.
3. Para endpoints protegidos:
   1. Ejecuta `POST /auth/login` con tu administrador de plataforma.
   2. Copia `accessToken` de la respuesta.
   3. En Scalar, configura la autenticación Bearer con ese token.

> El documento OpenAPI actual no declara un esquema de seguridad Bearer. Por eso Scalar no aplica el token automáticamente; hay que pegarlo a mano en cada petición protegida.

---

## Endpoints y ejemplos

Las rutas no tienen prefijo `/api`. Los errores siguen el formato `ProblemDetails`.

### Autenticación de plataforma: `/auth`

**Login** (anónimo, `platform-login`, 10 peticiones por minuto por IP):

```bash
curl -X POST http://localhost:5070/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@ejemplo.com","password":"<contraseña-de-12-o-más>"}'
```

Respuesta 200:

```json
{
  "accessToken": "eyJhbGciOi...",
  "tokenType": "Bearer",
  "accessTokenExpiresAtUtc": "2026-10-19T12:15:00Z",
  "refreshToken": "x7Qk...",
  "refreshTokenExpiresAtUtc": "2026-10-26T12:00:00Z"
}
```

**Refresh** (anónimo, `platform-refresh`):

```bash
curl -X POST http://localhost:5070/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken>"}'
```

**Perfil** (`ClientJwt`, requiere `ClientJwt:Authority` y `ClientJwt:Audience`):

```bash
curl http://localhost:5070/auth/me -H "Authorization: Bearer <token-del-idp>"
```

Devuelve `{id, email, displayName, tenants:[{id, name, roles:[{id, name, companies:[{id, name}]}]}]}`.

### Gestión de agentes (plataforma): `/platform/agents`

Requiere política `PlatformAdministrator`.

**Crear agente**:

```bash
curl -X POST http://localhost:5070/platform/agents \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"<guid>","companyId":"<guid>","name":"Caja Central"}'
```

Respuesta 201: `{"agentId":"<guid>","secret":"<secreto-una-sola-vez>"}`.

Errores: 409 si se alcanza `Plan.MaxAgents`, si la licencia no es válida o si el nombre ya existe en la empresa. 400 si la empresa no pertenece al tenant.

**Revocar agente**:

```bash
curl -X POST http://localhost:5070/platform/agents/<agentId>/revoke \
  -H "Authorization: Bearer <accessToken>"
```

Responde 204, o 404 si no existe. Marca el agente como inactivo y revoca todos sus refresh tokens.

### Credenciales del certificador (plataforma): `/platform/companies/{companyId}/certifier-credentials`

Requiere política `PlatformAdministrator` y usa el rate limit `PlatformAgentManagement`. La empresa debe tener ya una `CompanyCertifierAssignment`.

```bash
curl -X PUT http://localhost:5070/platform/companies/<companyId>/certifier-credentials \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{"credentials":{"usuario":"worker-gt","llaveApi":"..."},"useProduction":true}'
```

- `credentials` es un diccionario libre de 1 a 20 entradas (clave de 1 a 100 caracteres, valor de hasta 4000). Las llaves las define el provider del worker para ese certificador.
- Se guarda cifrado en `CredentialsEncrypted` y se registra un `AuditLog` con el admin y la IP. Las credenciales nunca se escriben en el log.
- Responde 204, 404 si la empresa no tiene asignación de certificador, 400 si una entrada no cumple los límites.
- No existe un GET: las credenciales solo se devuelven al worker.

### Agente (worker): `/agent`

**Handshake** (anónimo, `agent-handshake`, 10 por minuto por IP):

```bash
curl -X POST http://localhost:5070/agent/handshake \
  -H "Content-Type: application/json" \
  -d '{"agentId":"<guid>","secret":"<secreto>","workerVersion":"1.0.0"}'
```

Respuesta 200 con `accessToken`, `refreshToken`, `licenseState` (`Active`, `Grace` o `Suspended`), `handshakenAtUtc`, `tenantId` y `companyId`. 401 si las credenciales no son válidas, 403 si la licencia no es válida.

**Refresh de token de agente** (anónimo, `agent-refresh`):

```bash
curl -X POST http://localhost:5070/agent/token/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken>"}'
```

Respuesta 200 con `accessToken`, `refreshToken` y `licenseState`, el estado de licencia que el worker sincroniza en cada refresh. 401 si el refresh token no es válido o el agente está revocado, 403 si la licencia no es válida.

**Heartbeat** (`Agent`, `agent-heartbeat`):

```bash
curl -X POST http://localhost:5070/agent/heartbeat \
  -H "Authorization: Bearer <accessToken-agente>" \
  -H "Content-Type: application/json" \
  -d '{"workerVersion":"1.0.0","acknowledgedCommandIds":[]}'
```

Respuesta 200: `{serverTimeUtc, pendingCommands:[{id, type, payload, leaseExpiresAtUtc}]}`.

Un comando `ResendDocument` llega con `payload` como string JSON: `"{\"documentId\":\"<guid>\",\"idempotencyKey\":\"...\"}"`. El worker hace ack del comando enviando su id en `acknowledgedCommandIds` en el siguiente heartbeat.

**Configuración del certificador** (`Agent`, `agent-configuration`):

```bash
curl http://localhost:5070/agent/certifier-configuration \
  -H "Authorization: Bearer <accessToken-agente>"
```

El `tenantId` y el `companyId` salen del token, nunca del request. Respuesta 200:

```json
{
  "certifierCode": "INFILE",
  "countryCode": "GT",
  "endpointUrl": "https://certificador.gt.test/api",
  "requestFormat": 1,
  "useProduction": true,
  "credentials": { "usuario": "worker-gt", "llaveApi": "..." }
}
```

- `requestFormat` es el enum `CertifierRequestFormat` (`0` = Json, `1` = Xml).
- `credentials` llega en claro sobre TLS y viene vacío si aún no se han cargado. El worker las guarda cifradas en local.
- Errores: 401 si el agente está revocado (su access token sigue vigente hasta caducar, por eso se revisa en cada llamada), 403 si la licencia no es válida, 404 si la empresa no existe, 409 si no tiene asignación de certificador o está inactiva, 500 si las credenciales no se pueden descifrar (key ring de Data Protection perdido).

**Reportar estado de documentos** (`Agent`, `agent-document-status`):

```bash
curl -X POST http://localhost:5070/agent/documents/status \
  -H "Authorization: Bearer <accessToken-agente>" \
  -H "Content-Type: application/json" \
  -d '{
    "documents": [{
      "idempotencyKey": "factura-000123",
      "branchId": null,
      "documentType": "FACT",
      "series": "A",
      "number": "123",
      "status": "Certified",
      "authorizationNumber": "3F2504E0-4F89-11D3-9A0C-0305E82C3301",
      "errorCode": null,
      "errorMessage": null,
      "attemptCount": 1,
      "issuedAtUtc": "2026-10-19T15:00:00Z",
      "certifiedAtUtc": "2026-10-19T15:00:02Z"
    }]
  }'
```

- Entre 1 y 100 documentos por lote. Se hace upsert por `idempotencyKey` dentro del tenant del agente.
- `branchId` es opcional y debe pertenecer a la empresa del agente. Si no, el documento va en `rejected`.
- Un estado final (`Certified` o `Error`) limpia `resendRequestedAtUtc`.
- Respuesta 200: `{"accepted": 1, "rejected": [{"idempotencyKey": "...", "reason": "..."}]}`.
- Errores: 403 si la licencia no es válida, 401 si el agente es desconocido.

### Documentos (plataforma): `/platform/documents`

Requiere política `PlatformAdministrator`. Rate limit `platform-documents`.

```bash
# Listar, con filtros opcionales: tenantId, companyId, branchId, status, from, to, page, pageSize (≤ 100)
curl "http://localhost:5070/platform/documents?status=Error&page=1&pageSize=50" \
  -H "Authorization: Bearer <accessToken>"

# Detalle
curl http://localhost:5070/platform/documents/<documentId> -H "Authorization: Bearer <accessToken>"

# Reenvío manual
curl -X POST http://localhost:5070/platform/documents/<documentId>/resend -H "Authorization: Bearer <accessToken>"
```

El listado devuelve `{items:[...], page, pageSize, totalCount}`, ordenado por `issuedAtUtc` descendente.

Respuestas del reenvío:

| Código | Significado |
|---|---|
| 202 | Se creó el comando `ResendDocument` para el agente que reportó el documento |
| 404 | El documento no existe o no es visible para quien llama |
| 409 | El documento ya está `Certified`, ya hay un reenvío pendiente o el agente está inactivo |

### Documentos (tenant): `/tenant/{tenantId}/documents`

Los mismos tres endpoints, con el token del IdP (`ClientJwt`). Rate limit `tenant-documents`.
- Responde 403 si el usuario no tiene membresía activa en el tenant.
- Solo muestra los documentos de las empresas permitidas por sus roles.

```bash
curl "http://localhost:5070/tenant/<tenantId>/documents?status=Error" -H "Authorization: Bearer <token-del-idp>"
curl -X POST http://localhost:5070/tenant/<tenantId>/documents/<documentId>/resend -H "Authorization: Bearer <token-del-idp>"
```

### Flujo completo de ejemplo

1. `POST /auth/login` como administrador de plataforma. Obtienes `accessToken`.
2. `POST /platform/agents` con `tenantId`, `companyId` y `name`. Guardas `agentId` y `secret`.
3. `POST /agent/handshake` con `agentId` y `secret`. Guardas los tokens del agente.
4. El worker certifica y llama a `POST /agent/documents/status`, por ejemplo con un documento en `Error`.
5. `GET /platform/documents?status=Error` para ver el documento.
6. `POST /platform/documents/{id}/resend`. Responde 202.
7. `POST /agent/heartbeat` devuelve el comando `ResendDocument`. El worker reenvía y reporta `Certified`.

---

## Configuración

### `appsettings.json`

| Sección | Clave | Valor por defecto |
|---|---|---|
| `Jwt` | `Issuer` | `wis-fel-api` |
| `Jwt` | `PlatformAudience` | `wis-fel-platform` |
| `Jwt` | `AgentAudience` | `wis-fel-agent` |
| `Jwt` | `PlatformTokenMinutes` / `AgentTokenMinutes` | `15` / `15` |
| `Jwt` | `PlatformRefreshTokenDays` / `AgentRefreshTokenDays` | `7` / `30` |
| `ClientJwt` | `Authority`, `Audience` | vacíos (`/auth/me` no funciona) |
| `ClientJwt` | `RequireHttpsMetadata` | `true` |
| `AgentHeartbeat` | `CommandLeaseSeconds` | `30` |
| `AgentHeartbeat` | `MaxCommandsPerHeartbeat` | `20` |
| `DataProtection` | `KeyRingPath` | vacío (obligatorio fuera de `Development`) |
| `RateLimiting` | cada política: `PermitLimit`, `WindowSeconds` | ver tabla abajo |
| `AllowedHosts` | | `*` |

Políticas de rate limiting (`PermitLimit` / `WindowSeconds`):

| Política | Límite |
|---|---|
| `PlatformLogin` | 10 / 60 s |
| `PlatformRefresh` | 20 / 60 s |
| `AgentHandshake` | 10 / 60 s |
| `AgentRefresh` | 20 / 60 s |
| `ClientMe` | 60 / 60 s |
| `AgentHeartbeat` | 120 / 60 s |
| `AgentDocumentStatus` | 60 / 60 s |
| `AgentConfiguration` | 60 / 60 s |
| `PlatformAgentManagement` | 30 / 60 s |
| `PlatformDocuments` | 60 / 60 s |
| `TenantDocuments` | 60 / 60 s |

### Secretos requeridos (no van en el repositorio)

| Clave | Obligatoria | Notas |
|---|---|---|
| `ConnectionStrings:FelApi` | Sí | Cadena de conexión a SQL Server |
| `Jwt:SigningKey` | Sí | Base64 de al menos 32 bytes |
| `ClientJwt:Authority` / `Audience` | Para `/auth/me` y `/tenant/*` | URL del IdP y audiencia |
| `DataProtection:KeyRingPath` | Fuera de `Development` | Carpeta persistente del key ring. Sin ella no se pueden descifrar las credenciales de certificador tras un reinicio; respáldala y restringe sus permisos |

### Pipeline HTTP

`UseHttpsRedirection` → `UseRouting` → `UseAuthentication` → `UseRateLimiter` → `UseAuthorization` → `MapControllers`.

Scalar y OpenAPI solo se mapean en `Development`.

---

## Migraciones de base de datos

Las migraciones están en `wis-fel-api/fel-api/Data/Migrations/`. Desde la raíz del repositorio:

```bash
# Aplicar migraciones pendientes
dotnet ef database update --project wis-fel-api/fel-api

# Crear una nueva migración
dotnet ef migrations add <Nombre> --project wis-fel-api/fel-api --output-dir Data/Migrations
```

El proyecto de inicio es el mismo `fel-api`, así que la cadena de conexión debe estar en user-secrets.

---

## Tests

```bash
dotnet test wis-fel-api/wis-fel-api.slnx
```

Los tests usan EF Core InMemory, así que no necesitan SQL Server.

`ApiFactory` levanta la API en memoria con:
- Una clave de prueba y un límite de login de 2 por minuto.
- Una clave simétrica local para `ClientJwt`, que reemplaza al IdP externo y permite probar los endpoints de tenant.

Cobertura actual (25 tests):

- `AgentApiTests` (11): login solo para administradores, `/auth/me`, rate limit de login, handshake y heartbeat con comando, rotación de refresh tokens y rechazo de reuso, provisionamiento y revocación, carga de credenciales de certificador y entrega al worker (comprobando que en la base están cifradas), 409 cuando la empresa no tiene asignación, `licenseState` como texto en handshake y refresh, y 401 para un agente revocado con token todavía vigente.
- `DocumentApiTests` (3):
  - Upsert del estado y rechazo de una sucursal ajena.
  - Filtro por estado y reenvío desde plataforma, con el comando entregado en el heartbeat y 409 para `Certified` y para un reenvío duplicado.
  - Alcance de tenant por membresía y por empresas de los roles.
- `DevelopmentSeedTests` (1): el seed es idempotente y los GET de plataforma y tenant devuelven los datos.
- `SubscriptionStateCalculatorTests` (3), `TenantQuotaValidatorTests` (4), `CertifierConfigurationResolverTests` (2), `ClientProfileServiceTests` (1).

---

## Estado actual y limitaciones

- No hay endpoints CRUD para tenants, empresas, sucursales, planes, suscripciones, certificadores, usuarios ni códigos de activación. Esos datos se cargan directamente en la base.
- La certificación, los providers por país y certificador, y los reintentos automáticos viven en el repositorio del worker. Esta API no tiene `HttpClient` hacia certificadores.
- El key ring de Data Protection se guarda en disco sin cifrar (`DataProtection:KeyRingPath`). No hay Azure Key Vault ni cifrado con certificado, ni rotación de credenciales de certificador.
- Al cambiar las credenciales no se encola ningún comando para que el worker recargue su configuración: las toma en su siguiente pull.
- `ActivationCode` está modelado, pero no se usa. `AuditLog` solo registra cambios de credenciales de certificador; los reenvíos todavía no se auditan.
- La migración `WorkerOwnedCertification` **borra** los documentos existentes. Se reemplazó el modelo de payload y envíos por el de solo metadatos, y además se eliminó la tabla `DocumentDeliveryAttempts`.
- Sin Dockerfile, sin docker-compose y sin CI.
- `AGENTS.md` describe PostgreSQL, RS256 y ASP.NET Identity como parte del diseño. La implementación actual usa SQL Server y HS256 con un hasher propio.

---

## Contribuir

- Convenciones: los datos de tenant y empresa siempre salen del token. Las nuevas entidades van en `Domain/` y su configuración en `FelApiDbContext`.
- Antes de proponer cambios, ejecuta `dotnet test wis-fel-api/wis-fel-api.slnx`.
- Los commits y los push los hace el responsable del repositorio de forma manual.
- Plan de diseño completo: [`AGENTS.md`](AGENTS.md).
