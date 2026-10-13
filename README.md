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

"Arriba" te aleja de quien te mira; izquierda/derecha te hacen girar a su alrededor.

**Teclas de prueba:** F1 ayuda · F2 controles relativos a la cámara / tanque · F3 marcador cuando no te ves · F4 invierte el sentido del anillo · F5 reinicia.

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
```

## Próximos pasos

- **Probarlo en serio** con 2, 3 y 4 personas y decidir: ¿controles relativos a la cámara o tipo tanque (F2)? ¿marcador de ayuda sí o no (F3)?
- **Online** con Netcode for GameObjects + Unity Multiplayer Services (lobby / código de sala). La lógica del anillo no cambia: solo cada cliente renderiza su propia cámara.
- **Sensación Gang Beasts:** ragdoll activo, brazos que agarran de verdad, colgarse de cornisas.
- Estética, narrativa y mapas.
