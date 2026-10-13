# WIS FEL Worker

Worker de escritorio para las instalaciones cliente de WIS FEL. Su objetivo es conectar con SQL Server, detectar documentos emitidos por Aspel SAE a través de la cola local de FEL, enviarlos al API de certificación cuando esté disponible y reflejar el resultado en la base local.

## Responsabilidades previstas

- Leer de `Documents` en la base `FEL` los documentos que el trigger `FEL_EMITIDAS` registra con estado `PENDIENTE`.
- Consultar en la base SAE correspondiente a cada empresa las tablas con sufijo de empresa (por ejemplo, `FACTF01`, `FACTF02`) y reunir cabecera, emisor, receptor, detalle, impuestos y adendas para formar el modelo `documento`.
- Enviar documentos al API, aún pendiente de desarrollo, y actualizar en `FEL.Documents` el estado, UUID, serie y número SAT y el motivo de rechazo.
- Ejecutar comandos remotos autorizados: probar conexión SAE, reprocesar documentos y actualizar clientes SAE. La actualización de clientes solo se permite cuando la empresa tenga habilitada la capacidad `puedeActualizarClientes`.
- Reportar el latido del worker cada 30 segundos.
- Conservar XML y PDF en la tabla `Documents` cuando esa opción esté configurada.

Cada empresa SAE puede tener su propia base de datos, servidor y credenciales. El worker solo debe atender empresas configuradas para esa instalación. No debe escribir en SAE salvo al ejecutar el comando autorizado de actualización de clientes.

## Tecnologías

- **C# y .NET 10**.
- **Worker Service con Generic Host** para el proceso en segundo plano.
- **Entity Framework Core 10 y proveedor SQL Server** para el acceso futuro a SAE y FEL.
- **SQL Server** como motor de las bases SAE y FEL.
- **Docker** para distribuir y ejecutar una imagen Linux en PCs Windows, Linux y macOS sin instalar el runtime .NET 10 en el host.
- **EF Core migrations y SQL versionado** serán las opciones a evaluar para preparar una base `FEL` nueva, incluidas sus tablas y stored procedures. No se incluye esquema en este scaffold.

## Estado de este repositorio

Este repositorio contiene la configuración inicial: solución, proyecto Worker, dependencias base, documentación y archivos Docker. El proceso actual permanece inactivo esperando la implementación de la lógica. Todavía no incluye conexiones a bases de datos, entidades/modelos, extracción o envío de documentos, comandos, heartbeat, creación de esquema ni API.

La base `FEL` se considera existente en instalaciones actuales. La preparación de la base y sus objetos para nuevas implementaciones se agregará en una etapa posterior.

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

La base SAE/FEL debe estar en una instancia SQL Server accesible desde la red del contenedor. No uses `localhost` como servidor SQL desde dentro del contenedor, excepto si configuras explícitamente una ruta hacia el host; usa el DNS o IP alcanzable desde Docker y permite el tráfico SQL Server en la red/firewall. Este repositorio no incluye SQL Server ni define todavía variables de conexión.

## Compilar sin Docker

Se requiere el SDK de .NET 10:

```bash
dotnet restore WisFel.Worker.slnx
dotnet build WisFel.Worker.slnx
```

## Seguridad

No incluir en el repositorio ni en la imagen cadenas de conexión, usuarios o contraseñas de SQL Server, llaves de certificador, certificados P12 ni credenciales de empresas que esta instalación no atiende. La configuración segura de secretos por instalación y por empresa se definirá antes de implementar conexiones; no se deben pasar secretos como argumentos de build, ya que pueden quedar en las capas de la imagen.
