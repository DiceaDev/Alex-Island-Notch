# Hoja de ruta

## Fase 1 — Arquitectura base
- [x] Establecer la base del proyecto WinUI 3 / .NET 10
- [x] Configurar el nivel de ejecución `asInvoker`
- [x] Incorporar el contrato del ciclo de vida de módulos
- [x] Persistir la configuración por usuario
- [ ] Incorporar inyección de dependencias y host de módulos
- [ ] Incorporar registro estructurado y gestión de fallos
- [ ] Agregar proyecto de pruebas unitarias y compilación CI

## Fase 2 — Interfaz Dynamic Island
- [x] Crear la ventana flotante inicial
- [x] Centrar la isla en la parte superior de la pantalla principal
- [x] Mantener la ventana siempre visible y fuera de la barra de tareas
- [x] Eliminar el marco y la barra de título tradicionales
- [x] Implementar modo notch compacto integrado al borde superior
- [x] Expandir la isla al pasar el cursor y permitir fijar el panel con clic
- [x] Incorporar animación de escala y transición al expandir/contraer
- [x] Evitar parpadeo del hover mediante cierre diferido y cancelación al reingresar
- [ ] Incorporar gestión de múltiples monitores y cambios de DPI
- [ ] Incorporar tamaño, posición y apariencia configurables

## Fase 3 — Integración con Windows
- [ ] Servicio de sesiones multimedia mediante GSMTC
- [ ] Servicio de volumen mediante Windows Core Audio
- [ ] Integración con notificaciones de Windows
- [ ] Batería, red y estado básico del sistema
- [ ] Limpieza de suscripciones a eventos y recuperación de conexiones

## Criterios de liberación
Una fase se considera terminada cuando compila en Debug y Release, dispone de pruebas automatizadas relevantes, funciona sin elevación en una cuenta estándar y documenta sus limitaciones conocidas.
