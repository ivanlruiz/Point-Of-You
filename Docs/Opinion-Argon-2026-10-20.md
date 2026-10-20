# Opinión de Gemini (Argon) — 20/10/2026

Resumen de dos rondas en el chat "Feedback para Prototipo Point of You". Primero vio el juego (descripción + capturas) y después el informe de los 10 agentes ([Sugerencias-agentes-2026-10-20.md](Sugerencias-agentes-2026-10-20.md)).

## Ronda 1: el juego

- **Riesgos:** que "adelante" gire cuando se mueve el observador y termines caminando en curva; mareo si la cabeza del observador se bambolea; pantallas muertas cuando una pared te tapa; efecto dominó con 3-4 jugadores (acomodar a uno le mueve la cámara a otro).
- **Puzzles:** alzar y tirar a tu propio observador para "poner tu cámara" en altura; dibujos que solo se arman desde un punto (perspectiva forzada); enemigos que se congelan si alguien los mira; objetos que solo ve una cámara; zonas donde solo te ven por espejos o techo de vidrio.
- **Controles:** fijar la dirección mientras mantenés la tecla; horizonte estable; silueta tuya a través de las paredes (sin mostrar el entorno); bordes de color y lentes del color de a quién le das imagen; botón de "mirame acá".
- **Estética:** robots con webcams cruzadas (la lente es del color del jugador al que le da imagen) o sujetos de prueba con cascos VR y estática de TV cuando te tapan.
- **Referencias:** Screencheat; Siren y Second Sight (ver desde ojos ajenos); la misión en segunda persona de Driver: San Francisco; Chained Together; It Takes Two.

## Ronda 2: cruce con el informe de los agentes

**Coinciden:** "adelante con memoria", cámara antimareo (suavizado vertical más lento + tope de giro), que el anillo se lea sin texto, sacar la vista general con 3 jugadores, puzzles de perspectiva y de "existe si lo miran", reordenar el anillo dentro del nivel.

**Cambió de opinión:** para el 4° cuadro con 3 jugadores prefiere el diagrama del anillo de los agentes a su cámara de seguridad (funciona en cualquier mapa del editor).

**No está de acuerdo con el informe:**
- Ocultar el cuerpo del observador para su cámara (bug 7): te sentirías una cámara flotante. Propone adelantar el ojo 15-20 cm o un dithering solo al mirar muy hacia abajo.
- Poner el log de sesión como prioridad 2: primero arreglar los exploits; el log, para playtests a ciegas.
- La estética de "linternas": cara en rendimiento con 4 pantallas y frustrante en mapas oscuros.
- Sacar el marcador por defecto: si una pared te tapa, el marcador es lo que te deja decirle al otro para dónde moverse.

**Lo que se les escapó a los dos (editor de mapas):**
- Quien crea un mapa y lo prueba solo lo ve con la cámara de seguimiento: va a diseñar saltos que de a 2-4 no se ven. Propone un **observador maniquí** que se clava en una baldosa para probar líneas de visión.
- Piezas pensadas para la vista: además de la ventana (que ya existe), una **cortina/humo** que deja pasar el cuerpo pero tapa la vista.
- El exploit del checkpoint (R) rompe cualquier mapa de la comunidad: el checkpoint por jugador es vital.
- Hacer un `Snap()` de todas las cámaras después de armar un mapa online.

**Su top 5 para la próxima versión:**
1. Arreglar los rompe-juegos: `runInBackground`, `Snap()` + `Teleport` al reaparecer, checkpoint por jugador, menú de Esc.
2. Cámara y control antimareo: adelante con memoria, suavizado vertical, sombra circular bajo los pies.
3. Claridad del anillo: ojos del color de quien mira, marcos de color, marcador automático tras ~0,8 s tapado, diagrama del anillo con 3, cabeza que gira con la mirada.
4. Editor pensado para la mecánica: pieza que deja ver pero no pasar (ya está: Ventana), cortina, observador maniquí para probar solo.
5. Rehacer los 3 niveles base (piedras desalineadas, pretil en la cornisa, puerta angosta), idealmente con el propio editor.

## Ronda 3: mapas procedurales (diseño y generador de Argon)

Argon diseñó y escribió `Scripts/Maps/MapGenerator.cs` (6 tipos de sala). Después de dos versiones y dos correcciones mínimas de ubicación de placas, pasan los 27 tests. Lo que pidió mirar en el primer playtest:

1. **El mirador en Cornisa y Laberinto:** cuánto tardan en darse cuenta de que el pretil de 3 m les tapa la terraza y de que el observador tiene que subir a la torre o pasarela. Si guían a ciegas en vez de buscar altura, pintar las zonas de mirador de otro color.
2. **Falsas conexiones de canal entre salas:** hay 4 colores que se repiten en hasta 7 salas, y una puerta se abre con cualquier placa de su color en todo el mapa. Ver si un jugador rezagado o una caja olvidada en la sala 1 deja abierta por accidente una puerta del mismo color en la sala 5.
3. **Ritmo del Foso (dificultad 2 vs 3):** en la 3 el tabique alterna pared y ventana celda por medio. Ver si el camarógrafo acompaña paso a paso o si las columnas le cortan la vista en pleno salto y se siente injusto.
4. **El efecto espejo en Puentes y Balcones:** dos jugadores que se miran de frente a través del tabique tienen izquierda y derecha invertidas. Medir cuánto tardan en calibrar hacia qué lado saltar.
5. **Las placas de rescate:** cuando el primero pasa la puerta de salida, ver si entiende rápido que tiene que pisar la placa de rescate para liberar al que quedó sosteniendo la placa de atrás.

Pidió que le contemos qué semilla jugaron y en cuál de los 6 tipos de sala hubo más gritos.
