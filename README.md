# Alex Island

Una experiencia nativa de **Dynamic Island para Windows 11**, desarrollada con **C#, WinUI 3 y .NET 10 LTS**.

Alex Island busca integrar en una interfaz flotante y discreta información útil del sistema, controles y herramientas de productividad y desarrollo. La aplicación está diseñada para ejecutarse en la sesión del usuario actual, **sin requerir privilegios de administrador**.

El núcleo debe poder utilizarse sin conexión. Las integraciones externas serán módulos opcionales e independientes.

## Estado del proyecto

El desarrollo está organizado en etapas. Actualmente, el repositorio contiene la estructura inicial de la solución y una primera base de la ventana principal. La compilación y validación en Windows todavía están pendientes.

| Etapa | Alcance | Estado |
| --- | --- | --- |
| 1. Arquitectura base | Estructura de la solución, ciclo de vida de módulos, configuración y servicios comunes. | En desarrollo |
| 2. Interfaz Dynamic Island | Ventana flotante, estados compacto y expandido, animaciones, múltiples monitores y escalado DPI. | Prototipo inicial |
| 3. Integración con Windows | Sesiones multimedia, volumen, notificaciones y estado básico del sistema. | Pendiente |
| 4. Experiencia multimedia | Controles de reproducción, información del contenido, carátulas y visualizaciones. | Planificada |
| 5. Productividad | Calendario, reuniones, recordatorios y herramientas de organización. | Planificada |
| 6. Herramientas para desarrollo | Integraciones con Git, GitHub, Docker y herramientas de desarrollo. | Planificada |
| 7. Integraciones externas | Conexiones opcionales con Teams, Slack, Outlook y otros servicios. | Planificada |
| 8. Personalización y optimización | Apariencia, preferencias y optimización de recursos. | Planificada |
| 9. Instalación y distribución | Instalador, actualizaciones, seguridad y publicación de versiones. | Planificada |

## Funciones

- **Música:** controles, carátula, progreso y **letras sincronizadas** (LRCLIB). En el notch cerrado, la línea actual reemplaza al artista.
- **Sistema (IT):** CPU, memoria, disco, tiempo encendido y batería; conexión principal, IP, puerta de enlace, **VPN** y **latencia**. Botón para copiar los datos del equipo a un ticket.
- **Avisos (peeks):** red caída o recuperada, VPN, volumen, brillo, idioma del teclado, Bloq Mayús, cargador y auriculares.
- **Shelf:** arrastra archivos, carpetas o texto al notch para tenerlos a mano y sacarlos arrastrando.
- **Hábitos:** lista de hábitos con meta diaria (`Agua x8`), racha de días seguidos y los últimos 7 días de un vistazo. Se guarda solo en tu PC.
- **Productividad:** backlog de tareas, agenda con importación `.ics` y temporizador Pomodoro.
- **Bandeja y ajustes:** iniciar con Windows, ocultar en pantalla completa, mascota, notch mínimo, tamaño y cada tipo de aviso.

Navegación: pasa el cursor para abrir, clic para fijar y rueda del mouse sobre las pestañas para cambiar de página.

## Tecnologías

- **Lenguaje:** C#
- **Interfaz:** WinUI 3
- **Plataforma:** Windows 11 x64
- **Framework:** .NET 10 LTS
- **SDK de interfaz:** Windows App SDK
- **Integración nativa:** API de Windows y Win32

## Requisitos de desarrollo

- Windows 11 de 64 bits.
- SDK de .NET 10.
- Visual Studio 2026 o una herramienta compatible con .NET 10.
- Componentes de desarrollo de aplicaciones WinUI 3 / Windows App SDK.

## Compilar el proyecto

Clona el repositorio:

```powershell
git clone https://github.com/DiceaDev/Alex-Island-Notch.git
cd Alex-Island-Notch
```

Restaura las dependencias y compila la solución:

```powershell
dotnet restore .\AlexIsland.sln
dotnet build .\AlexIsland.sln -c Debug
```

Para generar una compilación Release:

```powershell
dotnet build .\AlexIsland.sln -c Release
```

## Ejecución sin privilegios de administrador

La aplicación debe instalarse y funcionar con los permisos del usuario que inició sesión en Windows.

Criterios de implementación:

- Ejecutar con nivel de privilegio `asInvoker`.
- Mantener la instalación en una ubicación del perfil del usuario, por ejemplo, `%LocalAppData%\Programs\AlexIsland`.
- Guardar configuración y datos modificables en `%LocalAppData%\AlexIsland` o `%AppData%`.
- No instalar servicios de Windows, controladores ni componentes que requieran elevación.
- No modificar claves de registro globales ni escribir en carpetas protegidas del sistema.
- Solicitar únicamente los permisos necesarios para cada integración opcional.

## Principios de arquitectura

- **Modularidad:** cada integración debe poder habilitarse, deshabilitarse y mantenerse de forma independiente.
- **Rendimiento:** priorizar eventos y operaciones asíncronas; evitar sondeos constantes y trabajo innecesario cuando la interfaz esté oculta.
- **Resiliencia:** aislar los errores de módulos opcionales para que no afecten al funcionamiento del núcleo.
- **Privacidad y seguridad:** proteger credenciales y no almacenar secretos en texto plano ni en el repositorio.
- **Compatibilidad:** considerar múltiples monitores, escalado DPI y cambios en el estado de Windows.

## Privacidad y conexiones de red

La app funciona sin conexión. Solo se conecta a Internet en estos casos:

| Función | Destino | Qué se envía | Cómo desactivarlo |
| --- | --- | --- | --- |
| Letras sincronizadas | `lrclib.net` (HTTPS) | Título, artista y duración de la canción | Ajustes → Música |
| Latencia | Host configurado (por defecto `1.1.1.1`) | Un ping ICMP cada 4 s, solo con la pestaña Sistema abierta | Ajustes → Sistema y red (puedes usar un host interno) |
| Importar calendario por enlace | La URL `https://` o `webcal://` que pegues | Una descarga de hasta 2 MB | Solo ocurre si lo pides |

Todo lo demás (tareas, agenda, shelf, hábitos, ajustes) se guarda solo en `%LocalAppData%\AlexIsland`. El shelf guarda rutas, no copias de archivos. El inicio automático usa la clave `Run` del usuario actual (HKCU).

## Seguridad

No subas al repositorio tokens de acceso, claves API, contraseñas, certificados privados ni archivos de configuración personal. Las integraciones externas deben solicitar el mínimo de permisos necesario y gestionar las credenciales mediante mecanismos seguros del sistema operativo.

## Licencia

Por definir.
