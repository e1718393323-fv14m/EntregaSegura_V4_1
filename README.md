# Entrega Segura (versión de clase)

Aplicación móvil en **.NET MAUI 9** para que un conductor registre las entregas de pedidos de papelería con **foto, firma y ubicación GPS**, conectada a **Supabase**.

## Pantallas

| Pantalla | Archivo | Qué hace |
| --- | --- | --- |
| Login | `Views/vLogin.xaml` | Inicia sesión con correo y contraseña (Supabase Auth). Puede recordar el correo |
| Principal | `Views/vPrincipal.xaml` | Saludo, número de entregas pendientes y realizadas, menú |
| Mis entregas | `Views/vEntregas.xaml` | Lista de órdenes pendientes del conductor |
| Detalle de la orden | `Views/vDetalleOrden.xaml` | Datos del cliente y productos; Cómo llegar (Google Maps) y Llamar |
| Confirmar entrega | `Views/vConfirmarEntrega.xaml` | Nombre de quien recibe, **foto (cámara)**, **firma (pantalla táctil)** y **GPS** |
| Novedad | `Views/vNovedad.xaml` | Motivo y descripción cuando no se pudo entregar, con foto opcional |
| Historial | `Views/vHistorial.xaml` | Órdenes entregadas o con novedad |
| Mi perfil | `Views/vPerfil.xaml` | Datos, rol, vehículo y resumen de la jornada |
| Menú supervisor | `Views/vSupervisor.xaml` | Resumen del equipo y barra de avance |
| Todas las órdenes | `Views/vTodasOrdenes.xaml` | Órdenes de todos los conductores con filtro por estado |
| Novedades | `Views/vNovedades.xaml` | Novedades del equipo; al tocarlas se registra la revisión |

## Roles

| Rol | Usuario de prueba | Qué puede hacer |
| --- | --- | --- |
| CONDUCTOR | `jperez@entregasegura.com`, `mlopez@entregasegura.com` | Ver sus entregas, confirmar con foto/firma/GPS, reportar novedades, historial y perfil |
| SUPERVISOR | `psalazar@entregasegura.com` | Resumen y avance de todo el equipo, todas las órdenes con filtro, revisar novedades y perfil |

Al iniciar sesión la app lee el rol en `uis.conductores` → `uis.roles` y abre `vPrincipal` (conductor) o `vSupervisor` (supervisor).

## Base de datos (10 tablas en el esquema `uis`)

| Tabla | Para qué sirve | Relación |
| --- | --- | --- |
| `roles` | CONDUCTOR o SUPERVISOR | — |
| `vehiculos` | Placa, modelo y capacidad | — |
| `conductores` | Perfil del usuario (mismo correo que en Supabase Auth) | → `roles`, → `vehiculos` |
| `clientes` | Nombre, RUC, dirección, teléfono y coordenadas | — |
| `productos` | Código, descripción y precio | — |
| `motivos_novedad` | Catálogo de motivos que se muestra en el Picker | — |
| `ordenes` | Orden de entrega del día y su estado | → `clientes`, → `conductores` |
| `detalle_orden` | Productos y cantidades de cada orden | → `ordenes`, → `productos` |
| `entregas` | Quién recibe, foto, firma y GPS (una por orden) | → `ordenes` |
| `novedades` | Motivo, descripción, foto y GPS; el supervisor la marca como revisada con un comentario | → `ordenes`, → `motivos_novedad` |

La app trae una orden con su cliente y sus productos en **una sola consulta**:
`ordenes?select=...,cliente:clientes(...),detalle:detalle_orden(cantidad,producto:productos(...))`.

## Cómo cumple el proyecto

| Requisito | Dónde |
| --- | --- |
| Servicio web con base de datos | Supabase con 9 tablas relacionadas en el esquema `uis`, consumidas en `Services/SupabaseService.cs` |
| Servicio web disponible | Google Maps para llegar al cliente (`vDetalleOrden`) |
| 3 sensores | Cámara (foto), GPS (ubicación) y pantalla táctil (firma) en `vConfirmarEntrega` |
| 5 pantallas además de login y menú | Mis entregas, Detalle, Confirmar entrega, Novedad, Historial y Mi perfil |
| Guarda datos | En Supabase (entregas, fotos y firmas) y en el teléfono (`Preferences` recuerda el correo) |
| Manejo de errores | `try/catch` con `DisplayAlert` en todas las pantallas y validación de campos vacíos |

## Pasos para ejecutar

1. **Supabase → SQL Editor**: ejecutar `supabase/script_entregasegura.sql` (borra y vuelve a crear las tablas de esta versión con datos de prueba).
2. **Project Settings → Data API → Exposed schemas**: verificar que esté `uis`.
3. **Authentication → Users**: crear `jperez@entregasegura.com`, `mlopez@entregasegura.com` y `psalazar@entregasegura.com` (marcar *Auto Confirm User*).
4. `Services/SupabaseService.cs` ya tiene la URL `https://psxxnkjmmlqsbiyygxnj.supabase.co` y su **Publishable key** (es pública por diseño; la seguridad la dan las reglas RLS).
5. Abrir `EntregaSegura.sln` en Visual Studio 2022, elegir **Windows Machine** o el emulador Android y presionar **F5**.

## Diseño (UX)
- **Sistema visual global** en `App.xaml`: paleta azul marino + naranja, tipografía (Titulo, Seccion, Etiqueta, Cuerpo, Suave), tarjetas, cajas de texto, *chips* de estado y botones Primario, Éxito, Secundario y Peligro (con estado deshabilitado).
- Encabezados de marca, indicadores en tarjetas, barra de avance animada y avatar con iniciales.
- Listas en tarjetas con número de parada, estado con color y mensajes amables cuando no hay datos.
- Acciones principales **fijas abajo** (al alcance del pulgar) en Detalle, Confirmar entrega y Novedad.
- Prueba de entrega en **5 pasos numerados** con lista de verificación ✔/○ que habilita el botón Guardar.

## Interacción con el usuario
- Login: casilla para **mostrar u ocultar la contraseña** y para recordar el correo.
- Confirmar entrega: una **lista de verificación** (✔/✘) se actualiza mientras se llena el nombre, la foto, la firma, el GPS y la conformidad; el botón *Guardar* se habilita solo cuando todo está completo.
- Novedad: **contador de caracteres** (rojo/verde) y sugerencia según el motivo elegido.
- Listas: se actualizan al deslizar hacia abajo y muestran el estado con color.

## Notas
- El proyecto usa **.NET 9** porque Visual Studio 2022 (17.14) no compila .NET 10. Por eso se usa `DisplayAlert` (en .NET 10 se llama `DisplayAlertAsync`).
- En Windows, si la PC no tiene cámara, el botón *Tomar foto* permite elegir una imagen.
- En el emulador Android se puede simular la ubicación en **⋯ → Location**.

---

## V4 · Rol administrador y recuperación de contraseña

**Instalación:** después de `script_entregasegura.sql`, cree en *Authentication → Users* el usuario `admin@entregasegura.com` (Auto Confirm) y ejecute `supabase/script_v4_admin_offline.sql`.

| Usuario | Rol | Pantalla inicial |
| --- | --- | --- |
| `admin@entregasegura.com` | Administrador | `vAdministrador` |
| `psalazar@entregasegura.com` | Supervisor | `vSupervisor` |
| `jperez@`, `mlopez@entregasegura.com` | Conductor | `vPrincipal` |

**Flujo:**
1. En el Login, el usuario pulsa **¿Olvidó su contraseña?** y escribe su correo. Se registra una solicitud en `uis.solicitudes_clave` (función `solicitar_recuperacion`, que responde igual exista o no el correo).
2. El **administrador** ve la solicitud en rojo en su panel, la toca (o toca al usuario en la lista) y asigna una **contraseña temporal** (la app sugiere una, p. ej. `Temp-4821-kq`). La función segura `admin_restablecer_clave` verifica el rol, cambia la clave en Supabase Auth, cierra las sesiones abiertas del usuario y deja auditoría en `uis.auditoria_claves`.
3. El administrador entrega la clave temporal por un medio seguro (en persona o por teléfono).
4. El usuario ingresa con la clave temporal y la app lo lleva a **`vCambiarClave`**, donde debe crear una nueva (mín. 8 caracteres, letras y números). Hasta hacerlo no puede usar el menú.

La llave publicable de la app **no** puede cambiar contraseñas de otros: solo la función de la base de datos, y solo si quien la llama tiene el rol ADMINISTRADOR.

## V4 · Modo offline

| Qué | Cómo funciona |
| --- | --- |
| Copia local | Cada vez que hay internet se guardan en el teléfono (carpeta privada de la app, por usuario) las órdenes, el perfil, los motivos y las novedades (`Services/OfflineService.cs`). |
| Sin internet | Las listas muestran la última copia. **Confirmar entrega** y **Novedad** funcionan igual: la foto, la firma y los datos se guardan en una **cola de pendientes** y la orden cambia de estado en la copia local. GPS y cámara no necesitan internet. |
| Ingreso sin internet | Si el usuario ya entró antes con internet en ese teléfono, puede ingresar offline. Se guarda una **huella SHA-256** de la contraseña (nunca la contraseña) y el *refresh token* en `SecureStorage`. |
| Sincronización | Al volver la señal (evento `Connectivity.ConnectivityChanged`), al ingresar, al abrir las listas o con el botón **Sincronizar** del inicio, la cola se envía en orden: sube fotos/firma, registra la entrega o novedad y actualiza la orden. Los reintentos no duplican archivos ni entregas. |
| Token vencido | Si el token expiró (1 hora), la app lo renueva sola con el *refresh token*. |
| Requiere internet | Revisar novedades (supervisor), administración de usuarios y restablecer contraseñas. |

**Cómo probarlo:** inicie sesión con internet como `jperez`, abra *Mis entregas*, active el **modo avión**, confirme una entrega (verá «Entrega guardada sin conexión» y el aviso naranja con 1 pendiente), desactive el modo avión y pulse **Sincronizar** (o espere unos segundos). La orden aparecerá como entregada en Supabase.
