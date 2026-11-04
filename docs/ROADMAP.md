# Hoja de ruta

## Fase 1 — Arquitectura base
- [x] Establecer la base del proyecto WinUI 3 / .NET 10
- [x] Configurar el nivel de ejecución `asInvoker`
- [x] Incorporar el contrato del ciclo de vida de módulos
- [x] Persistir la configuración por usuario
- [x] Dividir la ventana principal en archivos por función
- [ ] Incorporar inyección de dependencias y host de módulos
- [ ] Incorporar registro estructurado y gestión de fallos
- [x] Agregar proyecto de pruebas unitarias y compilación CI

## Fase 2 — Interfaz Dynamic Island
- [x] Crear la ventana flotante inicial
- [x] Centrar la isla en la parte superior de la pantalla principal
- [x] Mantener la ventana siempre visible y fuera de la barra de tareas
- [x] Eliminar el marco y la barra de título tradicionales
- [x] Implementar modo notch compacto integrado al borde superior
- [x] Expandir la isla al pasar el cursor y permitir fijar el panel con clic
- [x] Incorporar animación de escala y transición al expandir/contraer
- [x] Evitar parpadeo del hover mediante cierre diferido y validación de posición real del cursor
- [x] Eliminar bordes nativos WS_BORDER/WS_DLGFRAME y bordes extendidos WS_EX_WINDOWEDGE/CLIENTEDGE
- [x] Desactivar el contorno DWM para que las esquinas visibles dependan de XAML
- [ ] Validar visualmente en Windows 11 la integración al borde físico superior, la transparencia exterior y el suavizado de las esquinas
- [x] Calcular ventana y región en píxeles físicos según el DPI del monitor
- [x] Tamaño del notch, notch mínimo y mascota configurables
- [x] Ícono en bandeja, ventana de ajustes y ocultar en pantalla completa
- [x] Isla en todos los monitores (la completa sigue al cursor; notch liviano en los demás)
- [ ] Elegir la posición de la isla

## Fase 3 — Integración con Windows
- [x] Conectar sesión multimedia activa mediante GSMTC y leer metadatos, portada y línea de tiempo
- [x] Implementar controles de anterior, reproducir/pausar y siguiente
- [x] Letras sincronizadas desde LRCLIB
- [x] Avisos de volumen (Core Audio), brillo e idioma del teclado
- [x] Integración con notificaciones de Windows (UserNotificationListener por sondeo: el evento solo existe en apps MSIX)
- [x] Batería, red, VPN, latencia y estado básico del sistema (pestaña Sistema)
- [x] Shelf de archivos y textos
- [ ] Limpieza de suscripciones a eventos y recuperación de conexiones

## Criterios de liberación
Una fase se considera terminada cuando compila en Debug y Release, dispone de pruebas automatizadas relevantes, funciona sin elevación en una cuenta estándar y documenta sus limitaciones conocidas.
