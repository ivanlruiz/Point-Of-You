# Point of You: síntesis de 9 revisiones

> Las líneas citadas son las del código **actual**. Después de las revisiones entró el editor de mapas (`Scripts/Maps/`) y se corrieron casi todos los números de `GameManager.cs` y `PrototypeBuilder.cs`, así que acá los recalculé leyendo cada archivo.

## Diagnóstico

El código está prolijo y la mecánica escala sola. El anillo se arma por índice (GameManager.cs:215-224), los viewports se acomodan solos, el host es autoritativo y usa contadores para salto y reset (NetworkPlayer.cs:12-14), hay tests con bots y ya existe un editor de mapas.

El mayor riesgo es que **el primer playtest descarte la idea por motivos que no tienen que ver con la idea**:
- Hoy los tres puzzles se pueden resolver sin usar la mecánica. R teletransporta al checkpoint de otro, la puerta se traba con el cuerpo, las piedras forman un carril recto y la cornisa se ve desde el piso.
- La pantalla dividida y la vista general con 3 jugadores dejan espiar lo que se supone que no ves.
- La cámara se mueve por cosas que vos no hiciste: los saltos del observador, un "adelante" que depende de una cámara que se mueve sola y los vuelos al reaparecer. Es la receta del mareo.

El segundo riesgo es que el anillo es invisible: solo existe en el texto "visto por J2" (GameManager.cs:483-484).

Hay un punto ciego de los revisores. El editor de mapas arma checkpoints, puertas y placas con `Configure()` (MapRuntime.cs:163-180), y `NetworkLevelState.Scan` toma las piezas del mapa custom (NetworkLevelState.cs:78-81). Cualquier arreglo en Checkpoint, Door o PressurePlate, o cualquier interruptor nuevo, tiene que pasar también por ahí.

## Bugs a corregir ya

1. **Con R se saltean los puzzles 1 y 2, y la puerta 1 se traba con el cuerpo.**
   - Qué pasa: un solo jugador adentro de la zona hace avanzar el checkpoint de todo el equipo (Checkpoint.cs:45-49). Ese checkpoint solo sube (GameManager.cs:271-274) y R reaparece ahí sin ninguna condición (PlayerCharacter.cs:123-124 → GameManager.cs:254-258). La zona del Checkpoint 1 (PrototypeBuilder.cs:182-184) queda pasando la Puerta 1 (:133) e incluye la Placa B (:132). La del Checkpoint 2 (:185-187) queda pasando la Puerta 2 (:179) e incluye la Placa C (:178). Alcanza con que crucen uno o dos: los demás aprietan R y aparecen del otro lado.
   - La puerta: `Door` no baja si hay un Rigidbody en su próxima posición (Door.cs:78, 83-95). Uno parado en el vano la deja casi abierta, y el otro pasa al costado, porque el vano mide 2,5 m.
   - Arreglo: checkpoint **por jugador**, actualizando también ResetLevel (:282-289), Unregister y Join. Para la puerta, angostar el vano a ~1 m o decidir que trabarla vale.
   - Contradicción resuelta: Código y Mercado proponían "avanza solo si están todos adentro" (como GoalZone.cs:36-40). Me quedo con "por jugador": lo otro castiga al que cruza el foso solo y es frágil en mapas del editor.
   - Verificado: el checkpoint, leyendo el código; la puerta, por geometría (confirmarlo con un bot).
   *(chico / alto · Puzzles, Código, Mercado)*

2. **La cámara vuela a través de las paredes al reaparecer o cuando cambia el anillo.**
   - Qué pasa: `SetObserver` solo asigna (RingCamera.cs:65) y LateUpdate persigue la cabeza nueva con un SmoothDamp de 0,03 s sin tope (:17, :79). Un salto de 10 a 30 m se dibuja en 4 a 6 frames, atravesando geometría. Pasa con cada caída al foso (GameManager.cs:114 → :254-258), con F4 y con cada Register o Unregister (:170, :180).
   - Online: `Respawn` mueve el transform a mano (PlayerCharacter.cs:300-302) y NetworkTransform lo interpola como un movimiento más, aunque `NetworkTransform.Teleport` existe en el NGO instalado.
   - Arreglo: un `RingCamera.Snap()` (lo mismo que `Init`, :57-63), llamado desde SetObserver y Respawn para `player.View` y `player.LookTarget?.View`, más Teleport en el host.
   - Verificado leyendo el código.
   *(chico / alto · Mecánica, Accesibilidad, Netcode)*

3. **Hay agarres que sobreviven al respawn y no se pueden romper.**
   - Qué pasa: Respawn solo suelta el agarre propio (PlayerCharacter.cs:299). El loop que suelta a quien te tiene agarrado está solo en Unregister (GameManager.cs:177-178), y `Respawnable.ResetToStart` (Respawnable.cs:27-34) tampoco suelta la caja: el SpringJoint del otro queda estirado hasta el checkpoint.
   - Además, si el joint se rompe (PlayerCharacter.cs:291-295) con el botón apretado, TryGrab vuelve a agarrar en el frame siguiente (:126-127).
   - Arreglo: un helper `ReleaseGrabsOn(body)` llamado desde ambos respawns, más un flag de "soltá antes de volver a agarrar".
   - Verificado leyendo; lo fuerte que es el tirón hay que verlo en play.
   *(chico / medio · Código)*

4. **En local no hay salida: con un ESPACIO en el título te quedás sin acceso al online hasta cerrar el juego.**
   - Qué pasa: el título exige `players.Count == 0` (GameManager.cs:68), F5 vuelve a sumar a todos desde `rememberedInputs` (:102-103), F6 solo funciona online (:350) y Esc solo suelta el mouse (:361). Si un joystick se queda sin pila, su jugador queda como estatua y bloquea la meta (GoalZone.cs:38-39). Además, HandleJoining sigue activo durante el nivel (:110): un A suelto suma a alguien y reacomoda el anillo de todos.
   - Arreglo: `ClearLocalPlayers` (:314-326) ya vacía jugadores y `rememberedInputs`, así que alcanza con un menú de Esc (Seguir, Volver al inicio, Sacar este jugador, Salir). Para sumarse a mitad de nivel, pedir mantener el botón 1 s.
   - Verificado leyendo.
   *(chico / alto · HUD/onboarding, Código, Accesibilidad)*

5. **El online se congela sin avisar.**
   - Pausa sin foco: `runInBackground: 0` (ProjectSettings.asset:86) y nada lo cambia por código. Si el host hace Alt+Tab para pegar el código en Discord, el juego se congela para todos.
   - Timeouts: UnityTransport queda con los valores por defecto (60 intentos de 1 s para conectar, 30 s para dar la conexión por caída; NetworkParams.cs:31-47), porque `EnsureNetworkManager` no los toca (OnlineSession.cs:196-212).
   - Estado falso: como `Net.IsOnline` es `IsListening` (Net.cs:11), con una IP mala el título desaparece y la barra dice "conectado" (GameManager.cs:523) durante un minuto.
   - Arreglo: `runInBackground = true`, unos 10 intentos, unos 8 s de desconexión, mostrar "conectando…" mientras `!IsConnectedClient` y un latido del host.
   - Verificado en el código y en el paquete.
   *(chico / alto · Netcode)*

6. **F4 invierte el anillo solo en tu PC.**
   - Qué pasa: cambia el estático `ringReversed` (GameManager.cs:45, 344-348) sin chequear la red, y NetworkLevelState no replica nada del anillo (NetworkLevelState.cs:28-32). Con 3 o más online, el "visto por" y las cabezas dejan de coincidir entre PCs.
   - Arreglo: ignorar F4 online ya mismo. Después, un `NetworkVariable<bool>` que escribe el host, que de paso es la base de la idea de reordenar el anillo.
   - Verificado leyendo.
   *(chico / medio · Netcode, Código, Puzzles)*

7. **El cuerpo del observador tapa la vista, y pararse sobre su cabeza rompe la cámara.**
   - El cuerpo: para esa cámara solo se ocultan la cabeza y los ojos (RingCamera.cs:121-122; PrototypeBuilder.cs:267). El ojo está a +1,0 y la cápsula llega a +0,8 (:235-243), así que con FOV 70 (GameManager.cs:197) el propio cuerpo entra por abajo del cuadro desde unos 15° de inclinación y tapa toda mirada de más de ~50° hacia abajo. Es justo el caso del observador en la cornisa mirando al que está al pie.
   - La cabeza: no tiene collider (Visual lo borra, PrototypeBuilder.cs:401-405). El que se sube encima queda con el ojo del de abajo adentro de su cuerpo. La inclinación se clava en -85 (RingCamera.cs:111) y GetPlanarBasis cae al fallback (:70-71), que mirando hacia arriba invierte "adelante" 180°.
   - Arreglo: ocultar también Cuerpo y Pechera para esa cámara (las manos no, porque sirven de feedback), que DrawMarker ignore al observador en el Linecast (GameManager.cs:497-499) y calcular la base desde el yaw.
   - Contradicción resuelta: Mecánica sugería dejar el hombro visible como señal de "estoy en otra cabeza", pero en la cornisa impide el puzzle.
   - Verificado por geometría; falta confirmarlo en play.
   *(chico / medio · Código, Mecánica, Accesibilidad, Física)*

**Sin verificar o descartado:**
- Joystick anotado dos veces en `rememberedInputs`: `Contains` compara por referencia (GameManager.cs:154). Depende de cómo el Input System reusa el dispositivo.
- Lugar de sesión ocupado después de una caída: OnStopped anula la sesión sin LeaveAsync (OnlineSession.cs:316-320). Hacen falta 4 PCs para probarlo.
- Basura por frame en input y HUD: existe, pero no es un bug.

**Correcciones a los revisores:**
- Sí se usa `systemCopyBuffer` (GameManager.cs:635, MapEditor.cs:236).
- Sí hay forma de vaciar `rememberedInputs` (`ClearLocalPlayers`).

## Victorias rápidas

1. **"Adelante" con memoria mientras mantenés la dirección.** Hoy la base sale de `transform.forward` (RingCamera.cs:68-74), que incluye el mirar alrededor (:110-112) y persigue al dueño: tu movimiento gira la cámara y la cámara gira tu movimiento. Si pasás al lado del observador, "adelante" gira 180°. La propuesta es fijar el yaw de `aimRotation` cuando Move pasa de 0 a algo y volver a calcularlo al soltar (~0,15 s) o si girás el stick más de 40°, desde ComputeWishDirection (PlayerCharacter.cs:221-224) y SendLocalInput (NetworkPlayer.cs:106-111). Los dos revisores coinciden en sumarlo como tercer modo de F2 (GameManager.cs:22) y no reemplazar el actual hasta el playtest. *(chico / alto · Mecánica, HUD/onboarding)*

2. **Cámara cómoda: que el cuerpo del otro no maneje tu pantalla.** Con un suavizado de 0,03 s (RingCamera.cs:17), cada salto de 1,4 m del observador te sube y te baja la vista. Y `rotationSharpness` 16 sin tope (:18, :81) da latigazos de ~300°/s cuando el dueño pasa pegado. La propuesta es separar el suavizado en XZ (0,03 s) e Y (0,12-0,25 s, o mantener la altura del último apoyo mientras el observador está en el aire), limitar el giro con RotateTowards a ~240°/s y sumar un test `Comodidad_ObservadorSacudido`, cuidando la tolerancia de PrototypeTests.cs:40. *(chico / alto · Mecánica, Física, Accesibilidad)*

3. **Con 3 jugadores, sacar la vista general.** Se prende con n==3 (GameManager.cs:232-233) y muestra las piedras y la cornisa desde arriba: una vista de dios que con 2 o con 4 no existe. Conviene reemplazarla por un diagrama del anillo (J1→J2→J3, con colores, flechas y quién se perdió de vista) y el objetivo. *(chico / alto · Accesibilidad, Mecánica, Mercado)*

4. **Mostrar el rol de camarógrafo y avisar cuando te pierden.** En DrawViewportLabel (GameManager.cs:480-486) iría "tu cabeza filma a J3". Si la línea de tu ojo a LookTarget queda tapada más de ~0,8 s, aparece un aviso con el color del otro. La lógica ya existe en DrawMarker (:489-517), escondida detrás de F3: hay que sacarla a un `VisibilityOf(p)`, que además alimenta la telemetría. *(chico / alto · HUD/onboarding, Estética, Accesibilidad, Mercado)*

5. **Que el anillo se lea sin HUD: ojos del color de quien filma y borde de tu color.** En RefreshRing (GameManager.cs:215-224), teñir los ojos de cada cabeza con el color de su LookTarget ("J1 lleva los ojos de J2") y dejar la pechera con el color propio. DrawViewportFrame (:471-478) pasa de negro de 2 px a 6 px del color del dueño. *(chico / alto · Estética, HUD/onboarding, Accesibilidad)*

6. **Sombra bajo los pies y mano siempre visible.** El sol a 58°/-30° (PrototypeBuilder.cs:428) tira la sombra en diagonal, y sobre el foso cae al abismo. Un blob con raycast que se achique con la altura da la referencia de aterrizaje. La mano se apaga en Awake (PlayerCharacter.cs:90): mostrarla tenue siempre deja ver para dónde mirás y dónde vas a agarrar. *(chico / alto · Mecánica, Accesibilidad, Física)*

7. **FOV según el cuadro y rendimiento con 4 cámaras.** El FOV vertical está fijo en 70 (GameManager.cs:197): con 2 jugadores da ~64° horizontales (efecto túnel). Conviene calcularlo con `Camera.HorizontalToVerticalFieldOfView`. Además, PC_RPAsset tiene 4 cascadas de 2048 hasta 50 m (PC_RPAsset.asset:46, 57-58), depth y opaque texture obligatorias (:22-23) y vSync apagado (QualitySettings.asset:86): todo eso se multiplica por 4 cámaras. Para pantalla dividida conviene un asset con 2 cascadas y unos 30 m. *(chico / medio · Mecánica, Accesibilidad)*

8. **Menú online sin fricción.** Al crear la partida, copiar el código con `GUIUtility.systemCopyBuffer` (ya se usa en GameManager.cs:635). Que ENTER en el campo de código dispare Unirse (:589-592), y que haya un botón Cancelar mientras está ocupado (hoy `GUI.enabled = !busy` bloquea todo, :571). *(chico / medio · HUD/onboarding)*

## Próxima versión (en orden)

1. **Que la mecánica deje de ser opcional en los tres puzzles.**
   - Foso: según las cuentas del código, el salto llega a ~4,2 m, y las piedras 1-3-5 forman un carril en x≈-1,2 (PrototypeBuilder.cs:154-158) con huecos de 1,05, 2,7, 2,9 y 3,35 m. Alcanza con "adelante y saltar en el borde". La propuesta es alinear las piedras en x, variar los huecos en z, desnivelar un par y subir la pared del frente para que la ventana sea la única vista buena.
   - Cornisa: desde z=40, la línea al pecho del que está arriba pasa el borde a ~2,7 m, por encima de los 2,1 (:199), así que "desde abajo no se ve" (:219) no se cumple. La propuesta es un pretil de 1,3 m con un hueco alineado con la caja.
   - Sumar una sala de calibración antes de la puerta: "pisá la placa de tu color", con un `ownerIndex` opcional en PressurePlate.
   *(chico-medio / alto · Puzzles, HUD/onboarding)*

2. **Log de sesión siempre activo, con "segundos sin verte".** OnlineLauncher ya escribe JSONL cuatro veces por segundo, pero solo con argumentos `-pou-` (OnlineLauncher.cs:36, 156, 207). Que escriba siempre en persistentDataPath:
   - si cada uno se ve o no, por segundo;
   - las entradas a checkpoints;
   - los respawns por caída (GameManager.cs:114) separados de los de R (PlayerCharacter.cs:123);
   - el uso de LookOffset (RingCamera.cs:40);
   - la distancia al observador, F2/F3 y RTT.

   Antes de repartir builds, cambiar `companyName: DefaultCompany`. *(medio / alto · Mercado)*

3. **Menú de pausa con opciones de confort por jugador.**
   - Sensibilidad (hoy son constantes, PlayerInputSource.cs:37-38), invertir Y y recentrado rápido, normal o nunca (RingCamera.cs:26).
   - Agarre manteniendo o alternando, y FOV.
   - Modo de control por jugador, implementando `TankMode` (PlayerInputSource.cs:32) en lugar del F2 global (GameManager.cs:43). Guardado en PlayerPrefs.
   - De paso, darle mirar alrededor al jugador de las flechas (hoy devuelve cero, PlayerInputSource.cs:104-107) y sacar SHIFT como agarre principal, porque dispara las Teclas Especiales de Windows.

   *(medio / alto · HUD/onboarding, Accesibilidad)*

4. **Que la cabeza gire con la cámara de verdad.** Hoy apunta al pecho de LookTarget aunque quien usa esa cabeza esté mirando para otro lado (PlayerCharacter.cs:176-183). En local hay que seguir la rotación de `LookTarget.View`. Online, CameraYaw ya viaja a todos (NetworkPlayer.cs:11, 30) y falta sumar el pitch. Es comunicación no verbal: ves cómo tu amigo maneja tu cabeza. *(medio / alto · Mecánica, Estética)*

5. **Que el anillo se mantenga estable cuando alguien entra o sale.** Register y Unregister rearman el anillo al instante (GameManager.cs:170, 180), y NextFreeIndex rellena huecos (:184-189), así que el que entra cae en el medio y les cambia el observador a dos pantallas. La propuesta es mostrar 2 s "Ahora te mira J3" (junto con el Snap del bug 2), hacer esperar al recién llegado hasta el próximo checkpoint y que GetSpawn (:260-269) chequee que el lugar esté libre. *(medio / medio · Netcode, Accesibilidad, Mecánica)*

6. **Medir la latencia del cliente y recortar lo barato.**
   - Mostrar el RTT en la barra y en el log.
   - Subir TickRate a 60.
   - Mandar el input por RPC unreliable en vez de la NetworkVariable de NetworkPlayer.cs:30, que con lectura para todos lo reenvía a todos.
   - Activar `UseUnreliableDeltas` y `UseRigidBodyForMotion` (NetworkPlayer.prefab:671, 705), seteándolos en el builder (PrototypeBuilder.cs:273-278) para que no se pierdan al regenerar.
   - Para el foso: en el host, extender el coyote time (PlayerCharacter.cs:159) con RTT/2, hasta 0,2 s, para los clientes.

   *(medio / alto · Netcode)*

7. **Sonido, empezando por el propio cuerpo.** No hay ningún AudioSource, y el único listener está en el GameManager (GameManager.cs:92). El primer set puede usar disparadores que ya existen: pasos, salto, agarre, placa, puerta y meta. Hay que decidir dónde va el listener: online, en la cámara local (te oís desde la cabeza del otro) o en tu cuerpo (te ubica cuando no te ves). Es una decisión para el playtest, porque los revisores no coinciden. En pantalla dividida, sonido 2D paneado por cuadrante. *(medio / alto · Estética, Mercado)*

8. **Separar los colores de jugadores y de puzzles, y sumar una forma por jugador.** La placa y la puerta 1 (PrototypeBuilder.cs:33) chocan con J4 amarillo, la puerta 2 (:34) con J2 azul y la bandera (:471) con J3 verde (GameManager.cs:37). Además, rojo y verde es el par que confunde la deuteranopía. La propuesta: una paleta tipo Okabe-Ito para los jugadores, puzzles en tono neutro con un glifo compartido entre placa y puerta, y un símbolo por jugador en la espalda, en el HUD y en el marcador. *(chico-medio / medio · Estética, Accesibilidad)*

## Ideas de puzzles

1. **Aros de mirada.** La línea de cámara "ojo del observador → tu pecho" tiene que pasar por tu aro. El host la calcula sin red nueva con `ObserverOf` (GameManager.cs:207-213). Hace falta una base `Switch`, porque Door y NetworkLevelState hoy solo conocen PressurePlate (Door.cs:14, NetworkLevelState.cs:85).
   - Con 2 jugadores las dos líneas se cruzan en X: un aro alto solo lo atraviesa el que tiene al observador subido a la caja.
   - Un spot de color en cada cabeza hace visible el haz.
   - Variante de Mecánica: una "placa-ojo" que se activa si la tenés centrada en tu cámara.

   *(medio / alto · Puzzles, Mecánica)*

2. **Piedras que solo existen mientras alguien las mira.** Una piedra es sólida si cae en el cono de algún eslabón del anillo y no está tapada; se replica con un `platformBits` como el `plateBits` actual. Las columnas de las ventanas (PrototypeBuilder.cs:170-172) pasan a ser un peligro real, y el observador tiene que elegir distancia y ángulo. *(medio / alto · Puzzles)*

3. **Reordenar el anillo: "sillas de director" o placa de giro.** Placas en ronda que rearman quién mira a quién, o una placa que invierte el anillo mientras la pisás (un F4 dentro del nivel). Para 4 jugadores, una permutación arbitraria permite parejas (J1↔J2, J3↔J4) o reasignar el anillo en cada checkpoint. Antes hay que arreglar los bugs 2 y 6. Con 2 jugadores no cambia nada, así que estas salas son para 3 o más. *(grande / medio-alto · Puzzles, Mecánica, Mercado)*

4. **Foso para 4: dos caminos en paralelo.** En un anillo de 4, los pares J4→J1 y J2→J3 no comparten jugadores. Con dos líneas de piedras juegan todos, y nadie queda mirando sin hacer nada. *(chico-medio / medio · Accesibilidad)*

5. **Anamorfosis.** Un símbolo partido en cubitos que solo se arma desde una baldosa. Lo ve en su pantalla el jugador filmado, no el que está parado ahí, y lo tiene que dictar. Hace falta una lógica "exacta" en Door (Door.cs:12) y una semilla replicada. En pantalla dividida la asimetría se pierde, así que conviene probarlo online. *(medio / medio · Puzzles)*

## Apuestas grandes

1. **Física "Gang Beasts" por capas, no un ragdoll completo.** La recomendación de Física es avanzar por pasos, en este orden:
   - Paso 0: anclas de cámara separadas de la malla. EyePosition y ChestPosition (PlayerCharacter.cs:75-76) pasan a ser `transform.position + Vector3.up*h`, con un pivote `Visual` aparte.
   - Paso 1: flojera cosmética en LateUpdate, que también corre en los clientes (:174) y no suma red, más brazos "de fideo" con una sola mano física.
   - Paso 2: empujones con una autoridad de control que baja con los golpes (hoy un empujón muere en ~0,09 s, :141-145), velocidad del suelo, collider de cabeza y colgarse solo de bordes marcados.
   - Paso 3: caída usando la misma cápsula (soltar FreezeRotation, PrototypeBuilder.cs:232).

   Un ragdoll completo rompe todo lo que usa `transform.position` (GoalZone.cs:39, Checkpoint.cs:47, GameManager.cs:114). Ojo con una contradicción: el collider de cabeza deja apilarse a ~2,08 m, contra una cornisa de 2,1 m, y la "escalera humana" ya hoy hace opcional la caja pese al test de PrototypeTests.cs:141. Decidan si eso es una feature. *(grande / alto · Física, Código, Mecánica)*

2. **Predicción local del propio personaje, nunca autoridad del dueño.** Solo si la medición da más de ~120 ms. Pasar a `AuthorityMode` Owner (NetworkPlayer.prefab:669) deja al cliente cinemático en el host y rompe placas (PressurePlate.cs:56), puertas (Door.cs:92) y agarre (PlayerCharacter.cs:258). Mercado plantea la hipótesis de que, al verte de lejos, la demora molesta menos: midan primero. *(grande / alto · Netcode, Mercado)*

3. **Una dirección estética que cuente el anillo.** Hay tres opciones:
   - A, "Circuito Cerrado": un instituto de vigilancia, donde la vista general encaja como sala de control.
   - B, "Ojos Prestados": muñecos de fieltro. Es la más barata, porque el arte actual ya va por ese lado.
   - C, "Linternas": la luz de tu cabeza alumbra al que filmás. Es la única que vuelve mecánica a la estética (perderte de vista es quedarte a oscuras), pero cuesta cuatro spots con sombra en URP.

   B si quieren llegar rápido a una demo; C si quieren que el arte sea parte del juego. *(grande / alto · Estética)*

4. **Canales de comunicación dentro del juego.**
   - Nombres replicados en vez de "J3" (PlayerCharacter.cs:84).
   - Un botón "¡no me veo!" que hace brillar la cabeza de tu observador.
   - Cuando te pierden de vista, los ojos de esa cabeza se entrecierran y su joystick vibra.
   - Más adelante, voz por proximidad con Vivox, con el oído en la cabeza del observador.

   *(medio-grande / alto · Mercado, Estética)*

5. **Una demo para quien juega solo, y partidas públicas.** Jugando solo hoy hay una cámara de seguimiento (RingCamera.cs:84), y todas las sesiones son privadas (OnlineSession.cs:87). La propuesta es un tutorial de 2 a 3 minutos con un observador bot, armado sobre `ScriptedInputSource` (PlayerInputSource.cs:234-266), más un "Unirse rápido". *(grande / alto · Mercado)*

6. **Posicionamiento y calendario.** Según Mercado, "te ves desde otro" ya existe para 2 jugadores (Another Eye, Pers-pective). Lo propio de este juego es la ronda de 3-4 con física y online, y el tráiler debería mostrar el 2x2. El orden que propone: itch ahora, página de Steam cuando haya una sala con arte y sonido, y Next Fest de junio 2027 en lugar del de febrero. Las fechas hay que reconfirmarlas con Valve. *(grande / alto · Mercado)*

## Plan de playtest

- **Antes de jugar:**
  - Arreglar los bugs 1, 2, 4 y 5, que ensucian los datos, y prender el log de sesión.
  - Validar con un bot que "solo adelante" se cae en el foso nuevo.
  - Sacarles F2, F3 y F4 a los jugadores: el facilitador fija la variante de cada sesión (por ejemplo con `-pou-variant`) y alterna el orden entre grupos.
- **Condición principal: una pantalla por persona,** online o con varias PCs por IP directa en la misma sala. La pantalla dividida queda como condición de control, porque cada uno puede mirar el cuadro que se filma desde su propia cabeza (GameManager.cs:240-250).
- **Grupos de 2, 3 y 4 por separado,** sin explicar nada más que la pantalla de título.
- **Qué comparar:**
  - Controles relativos, con memoria y tanque.
  - Marcador apagado, automático a los 1,5 s y siempre prendido.
  - Online: jugar el foso dos veces, una con el host cruzando y otra con un cliente.
  - Opcional (Mercado): que el stick del observador gire tu vista, como en Another Eye.
- **Qué medir con el log:**
  - Porcentaje del tiempo sin verte y rachas de más de 10 s. Cerca de 0% significa que la mecánica no muerde; rachas largas, frustración.
  - Tiempo por zona.
  - Respawns por caída y por R.
  - Porcentaje del tiempo con LookOffset mayor a 10°.
  - Distancia al observador, RTT y cuántas veces apagan el marcador.
- **Qué observar y preguntar:**
  - El minuto del primer "movete que no me veo" y si se lo dicen al observador correcto (con 3-4 jugadores: ¿leen el "visto por"?).
  - Mareo del 1 al 5 por zona, y demora del 1 al 5 cruzada con el RTT.
  - Al cierre: ¿cuándo te reíste?, ¿cuándo te quisiste ir?, ¿con quién lo jugarías y pagarías?
- **Criterios de decisión escritos antes de jugar.** Ejemplos:
  - Si el marcador baja la frustración pero también baja la comunicación, queda como opción de accesibilidad y no por defecto.
  - Si "adelante con memoria" baja el mareo sin bajar la comunicación, pasa a ser el modo por defecto.
