# Point of You

Juego de puzzles cooperativo en 3D (en la línea de Gang Beasts / Human Fall Flat) con una sola regla rara:
**nunca ves desde tus ojos. Tu pantalla te muestra a vos, desde la cabeza de otro jugador.**

- Con 2 jugadores: yo te miro a vos y vos me mirás a mí.
- Con 3 o más es una ronda: J1 mira a J2, J2 mira a J3… y el último mira a J1.
- La cámara está fija en la cabeza del otro y siempre te apunta. Si una pared se mete en el medio, te perdés de vista.

Este repo es la **beta de la mecánica**: sala gris, personajes cápsula, 3 puzzles. Estética, narrativa y mapas vienen después.

| 2 jugadores | 3 jugadores (+ vista general) |
|---|---|
| ![2 jugadores](Docs/2-jugadores.png) | ![3 jugadores](Docs/3-jugadores.png) |

![4 jugadores: J4 cruza las piedras y lo ve J3 por la ventana](Docs/4-jugadores-ventana.png)
*Abajo a la derecha: la pantalla de J4, que cruza el foso, filmada desde la cabeza de J3, que lo mira por una ventana del pasillo.*

## Cómo jugar

1. Abrir la carpeta con **Unity 6.3 (6000.3.14f1)**. La primera vez se abre sola la escena `Assets/_Project/Scenes/Prototype.unity`.
2. Darle a **Play** y sumarse (hasta 4 en la misma PC):

| | Sumarse | Mover | Saltar | Agarrar (mantener) | Reaparecer |
|---|---|---|---|---|---|
| Teclado izquierdo | ESPACIO | WASD | ESPACIO | SHIFT IZQ / E | R |
| Teclado derecho | ENTER | Flechas | ENTER / NUM 0 | SHIFT DER / CTRL DER | BORRAR |
| Joystick | A / Start | Stick / cruceta | A | Gatillos / RB / X | Select |

"Arriba" te aleja de quien te mira; izquierda/derecha te hacen girar a su alrededor. Mientras mantenés una dirección, "adelante" queda fijo aunque la cámara gire (se recalcula al soltar).

Con el nivel en marcha, para sumarse hay que **mantener** ESPACIO / ENTER / A un momento. **Esc** abre la pausa: seguir, volver al inicio, sacar a un jugador o salir. Si un joystick se desconecta, su jugador sale solo.

**Mirar alrededor:** el mouse (lo usa el jugador de WASD) o el stick derecho giran la vista desde la cabeza del otro. Cuando dejás de moverlo, la cámara vuelve sola a apuntarte. "Adelante" siempre es hacia donde mira la cámara. Durante el juego el mouse queda capturado (Esc abre la pausa y lo suelta).

### Online (una persona por PC)

En la pantalla de inicio:

- **Crear partida online** → aparece un código arriba (ej. `PKPRCB`). Pasáselo a los demás.
- **Código + Unirse** → entrás a la partida de otro. Funciona desde cualquier casa, sin abrir puertos (Unity Relay).
- **IP directa** → Hostear / Conectar por IP, para la misma red o pruebas. Por internet habría que abrir el puerto 7777.

Cualquier control de tu PC (teclado o joystick) maneja a tu jugador. **F5** reinicia el nivel (solo el host) y **F6** sale de la partida. La barra de arriba muestra tu **ping** con el host.

Cómo funciona: quien crea la partida (host) simula toda la física. Los demás mandan sus controles y la orientación de su cámara, y reciben las posiciones. Por eso el host juega sin demora y los demás ven a su personaje con un pequeño retraso: unos 145 ms en la misma red, más el ping. Los controles viajan apenas cambian (sin esperar el tick de red), el estado se manda 60 veces por segundo y, a quien tiene ping alto, el host le da más margen para saltar justo al borde.

Para probar sin otra PC, dentro de Unity: *Window → Multiplayer → Multiplayer Play Mode*. Activás un jugador virtual, que abre otra ventana del juego; en una creás la partida y en la otra te unís con el código.

El proyecto está vinculado al proyecto **PointOfYou** de Unity Cloud (org *ivanlruiz*). Las sesiones y Relay corren ahí y tienen un nivel gratuito.

**Teclas de prueba:** F1 ayuda · F2 controles: cámara con memoria (por defecto) / cámara / tanque · F3 marcador cuando no te ves · F4 invierte el sentido del anillo (solo en local) · F5 reinicia.

## Editor de mapas

Desde la pantalla de inicio: **Editor de mapas**. Los mapas se arman en una grilla de 1 m con estas piezas:

| Tecla | Pieza | |
|---|---|---|
| 1 | Piso | arrastrá para pintar un rectángulo |
| 2 | Pared | 3 m de alto |
| 3 | Ventana | se ve a través, no se pasa |
| 4 | Bloque | 1 m de alto (se sube con la caja, no saltando desde el piso si está apilado) |
| 5 | Piedra | piso de otro color, para caminos de salto |
| 6 | Placa | abre las puertas de su mismo **color** (C lo cambia) que le quedan más cerca; las de ese color en otra punta del mapa no |
| 7 | Puerta | se abre mientras esté pisada alguna de sus placas (las de su color más cercanas); R la gira |
| 8 | Caja | se puede agarrar y arrastrar |
| 9 | Inicio | donde aparecen los jugadores (uno solo) |
| 0 | Checkpoint | punto de reaparición |
| - | Meta | se completa cuando están todos adentro |

Clic izquierdo pone, clic derecho borra, **PgUp/PgDn** cambia la altura (cada nivel son 0,5 m), WASD mueve la cámara, Q/E o rueda del medio la giran, la rueda hace zoom, **Ctrl+Z** deshace, **Ctrl+S** guarda y **P** prueba el mapa con jugadores locales (**F10** vuelve al editor).

- **Guardar / Abrir**: los mapas quedan en esta PC (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\PointOfYou\Mapas`).
- **Copiar código**: copia el mapa como texto (`POY1:…`) para pasarlo por chat. El otro lo usa con **Pegar código de mapa** en la pantalla de inicio.
- **Online**: se juega el mapa elegido por quien crea la partida; a los demás les llega solo.

## Mapas procedurales

En la pantalla de inicio: **Generar mapa procedural**, eligiendo jugadores (2-4), dificultad (1-3) y, si querés, una semilla (vacía = al azar). La misma semilla con la misma configuración da siempre el mismo mapa. El mapa generado se guarda como cualquier otro: se juega local u online y se comparte con su código. En el editor, **Generar** reemplaza el mapa por uno procedural para retocarlo a mano (Ctrl+Z lo deshace).

El diseño y el generador (`Scripts/Maps/MapGenerator.cs`) los hizo **Gemini (Argon)**: encadena salas de 6 tipos, cada una pensada para que haga falta la cámara del otro.

| Sala | Qué hay que hacer |
|---|---|
| Esclusa con muro de oclusión | Uno pisa la placa para abrir la puerta; detrás hay un muro, así que el que se queda atrás tiene que ir a una ventana para ver al que cruzó |
| Foso con galería de ventanas | Uno cruza piedras en zigzag; el otro lo acompaña por una galería y le da vista de costado |
| Cornisa con pretil | Se sube con la caja a 2 m; desde abajo no se ve arriba, así que el observador sube a un mirador |
| Puentes paralelos | Dos carriles separados por ventanas; la placa de uno abre la puerta del otro |
| Laberinto ciego con caja | Solo desde una pasarela alta se puede guiar al que lleva la caja |
| Balcones y foso central | Los de los balcones abren el paso y guían al que salta en el medio |

Las pruebas `ProcGenTests` verifican el generador: determinismo, 180 combinaciones válidas que se arman sin errores, acceso geométrico a cada puerta y placa, y bots que comprueban que cada placa abre su puerta y que la meta se completa.

## El nivel

1. **La puerta.** Dos placas amarillas, una de cada lado, abren la puerta mientras alguien las pisa.
2. **Las ventanas.** Uno cruza el foso saltando por piedras. Los demás lo acompañan por un pasillo seguro y lo miran por ventanas: la vista lateral es la que permite calcular los saltos. La placa celeste del otro lado abre el pasillo.
3. **La cornisa.** Hay que subir todos a la bandera usando la caja. Desde abajo no se ve la cornisa, así que el que sube depende de que su observador también busque altura.

## Código

```
Assets/_Project/
  Scripts/Core/GameManager.cs        ingreso de jugadores, anillo, pantalla dividida, checkpoints, HUD
  Scripts/Camera/RingCamera.cs       la cámara en la cabeza del otro (oculta esa cabeza solo para esa cámara)
  Scripts/Player/PlayerCharacter.cs  cápsula física: mover, saltar, agarrar (SpringJoint)
  Scripts/Input/PlayerInputSource.cs teclado partido en dos, joysticks y bots para tests
  Scripts/Puzzles/                   placas, puertas, checkpoints, meta, objetos que reaparecen
  Scripts/Online/OnlineSession.cs    crear / unirse (código o IP), spawnea jugadores y el estado del nivel
  Scripts/Online/NetworkPlayer.cs    input del dueño → host (incluye hacia dónde mira su cámara)
  Scripts/Online/NetworkLevelState.cs placas, puertas, checkpoint y meta replicados
  Scripts/Online/OnlineLauncher.cs   argumentos -pou-* para probar host + cliente con builds y bots
  Editor/PrototypeBuilder.cs         genera materiales, prefab del jugador y la escena
  Tests/PlayMode/PrototypeTests.cs   tests de la mecánica con jugadores bot
```

La escena **se genera por código**: menú *Point of You → Reconstruir escena prototipo* (o por línea de comandos, abajo).
Rehacerla pisa los cambios hechos a mano. Para iterar el nivel, editar `PrototypeBuilder.cs`. Cuando pasemos a diseñar mapas en el Editor, se deja de usar el builder.

```bash
# Regenerar la escena sin abrir Unity
unity run . -- -executeMethod PointOfYou.EditorTools.PrototypeBuilder.Build

# Tests (anillo de cámaras, movimiento, salto, agarre, placas, reaparición + capturas en TestResults/)
unity test . --mode PlayMode

# Build de Windows (queda en Builds/, que no se sube a git)
unity build . --target StandaloneWindows64 --output-path Builds/Windows/PointOfYou.exe

# Build de Linux (necesita el módulo "Linux Build Support (Mono)" en Unity)
unity build . --target StandaloneLinux64 --output-path Builds/Linux/PointOfYou.x86_64
```

En Linux, el juego se reparte como `.tar.gz` y no como `.zip`, para que conserve el permiso de ejecución:

```bash
tar xzf PointOfYou-Linux.tar.gz
./PointOfYou/PointOfYou.x86_64
```

Para jugar online entre Windows y Linux, los dos builds tienen que salir del mismo commit.

Prueba online automática con dos procesos (host y cliente manejados por bots):

```bash
Builds/Windows/PointOfYou.exe -batchmode -pou-host 7777 -pou-loopback -pou-bot "6:tp=-3.5;0.9;9" -pou-report host.jsonl -pou-quit 20
Builds/Windows/PointOfYou.exe -batchmode -pou-client 127.0.0.1 7777 -pou-bot "1:up,2.5:stop,3.5:jump" -pou-report client.jsonl -pou-capture cliente.png 7 -pou-quit 18
```

Con `-pou-code-host <archivo>` / `-pou-code-join <archivo>` hace lo mismo con código de partida (Relay).

## Próximos pasos

- **Probarlo en serio** con 2, 3 y 4 personas y decidir: ¿controles relativos a la cámara o tipo tanque (F2)? ¿marcador de ayuda sí o no (F3)?
- **Online:** jugarlo con gente real y ver cuánto molesta el retraso del propio personaje en los clientes (~145 ms + ping). Si molesta, el paso siguiente es que cada cliente simule su propio personaje. Después: reconexión, migración de host, pantalla dividida + online combinados.
- **Sensación Gang Beasts:** ragdoll activo, brazos que agarran de verdad, colgarse de cornisas.
- Estética, narrativa y mapas.
