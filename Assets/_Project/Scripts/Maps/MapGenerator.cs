using System;
using System.Collections.Generic;
using UnityEngine;

namespace PointOfYou
{
    public class GeneratorSettings
    {
        public int players = 2;    // 2..4
        public int rooms = 5;      // cantidad de salas entre el Inicio y la Meta
        public int difficulty = 2; // 1..3
    }

    public static class MapGenerator
    {
        private const int RoomWidth = 12; // x va de -6 a +5 (12 celdas)
        private const int MinX = -6;
        private const int MaxPlatesAllowed = 31;

        private enum RoomArchetype
        {
            TwinAirlock = 0,
            SideWindowPit = 1,
            BlindLedge = 2,
            TwinBridges = 3,
            BoxBastion = 4,
            CrossGazeSlalom = 5
        }

        /// <summary>
        /// Contexto interno de construcción para evitar superposiciones de piezas,
        /// administrar canales (0..3) y controlar límites de coordenadas y placas.
        /// </summary>
        private class BuildContext
        {
            public readonly MapData map;
            public readonly System.Random rng;
            public readonly int players;
            public readonly int rooms;
            public readonly int difficulty;

            public int currentZ;
            public int nextChannel;
            public int plateCount;

            private readonly HashSet<Vector3Int> occupiedSolids = new HashSet<Vector3Int>();

            public BuildContext(MapData map, System.Random rng, GeneratorSettings s)
            {
                this.map = map;
                this.rng = rng;
                this.players = Mathf.Clamp(s?.players ?? 2, 2, 4);
                this.rooms = Mathf.Clamp(s?.rooms ?? 5, 1, 7);
                this.difficulty = Mathf.Clamp(s?.difficulty ?? 2, 1, 3);
                this.currentZ = 0;
                this.nextChannel = 0;
                this.plateCount = 0;
            }

            public int AllocateChannel()
            {
                int ch = nextChannel % 4;
                nextChannel++;
                return ch;
            }

            public void AddFloor(int x, int y, int z, int w, int d, PieceType floorType = PieceType.Floor)
            {
                if (w <= 0 || d <= 0) return;
                y = Mathf.Clamp(y, 0, MapData.MaxLevel);
                map.pieces.Add(new MapPiece
                {
                    type = floorType,
                    x = x,
                    y = y,
                    z = z,
                    w = w,
                    d = d,
                    rot = 0
                });
            }

            public void AddBlock(int x, int y, int z, int w = 1, int d = 1)
            {
                if (w <= 0 || d <= 0) return;
                y = Mathf.Clamp(y, 0, MapData.MaxLevel - 2);
                map.pieces.Add(new MapPiece
                {
                    type = PieceType.Block,
                    x = x,
                    y = y,
                    z = z,
                    w = w,
                    d = d,
                    rot = 0
                });
            }

            /// <summary>
            /// Construye una plataforma maciza de bloques de 1 m (2 niveles cada capa) desde baseLevel.
            /// </summary>
            public void AddPlatformBlocks(int x, int baseLevel, int z, int w, int d, int layersOf1m)
            {
                for (int i = 0; i < layersOf1m; i++)
                {
                    AddBlock(x, baseLevel + i * 2, z, w, d);
                }
            }

            public void AddWall(int x, int y, int z, int w = 1, int d = 1)
            {
                if (w <= 0 || d <= 0) return;
                y = Mathf.Clamp(y, 0, MapData.MaxLevel);
                map.pieces.Add(new MapPiece
                {
                    type = PieceType.Wall,
                    x = x,
                    y = y,
                    z = z,
                    w = w,
                    d = d,
                    rot = 0
                });
            }

            public void AddWindow(int x, int y, int z, int w = 1, int d = 1)
            {
                if (w <= 0 || d <= 0) return;
                y = Mathf.Clamp(y, 0, MapData.MaxLevel);
                map.pieces.Add(new MapPiece
                {
                    type = PieceType.Window,
                    x = x,
                    y = y,
                    z = z,
                    w = w,
                    d = d,
                    rot = 0
                });
            }

            public bool AddPlate(int x, int y, int z, int channel)
            {
                if (plateCount >= MaxPlatesAllowed) return false;
                y = Mathf.Clamp(y, 0, MapData.MaxLevel);
                var key = new Vector3Int(x, y, z);
                if (occupiedSolids.Contains(key)) return false;
                occupiedSolids.Add(key);

                map.pieces.Add(new MapPiece
                {
                    type = PieceType.Plate,
                    x = x,
                    y = y,
                    z = z,
                    w = 2,
                    d = 2,
                    rot = 0,
                    channel = Mathf.Clamp(channel, 0, 3)
                });
                plateCount++;
                return true;
            }

            public void AddDoor(int x, int y, int z, int rot, int channel)
            {
                y = Mathf.Clamp(y, 0, MapData.MaxLevel);
                map.pieces.Add(new MapPiece
                {
                    type = PieceType.Door,
                    x = x,
                    y = y,
                    z = z,
                    w = 1,
                    d = 1,
                    rot = ((rot % 4) + 4) % 4,
                    channel = Mathf.Clamp(channel, 0, 3)
                });
            }

            public void AddBox(int x, int y, int z)
            {
                y = Mathf.Clamp(y, 0, MapData.MaxLevel);
                var key = new Vector3Int(x, y, z);
                if (occupiedSolids.Contains(key)) return;
                occupiedSolids.Add(key);

                map.pieces.Add(new MapPiece
                {
                    type = PieceType.Box,
                    x = x,
                    y = y,
                    z = z,
                    w = 1,
                    d = 1,
                    rot = 0
                });
            }

            public void AddCheckpoint(int x, int y, int z)
            {
                y = Mathf.Clamp(y, 0, MapData.MaxLevel);
                map.pieces.Add(new MapPiece
                {
                    type = PieceType.Checkpoint,
                    x = x,
                    y = y,
                    z = z,
                    w = 3,
                    d = 3,
                    rot = 0
                });
            }

            /// <summary>
            /// Agrega paredes perimetrales laterales (izquierda en x=-6 y derecha en x=+5).
            /// </summary>
            public void AddSideWalls(int zStart, int depth, int y = 0)
            {
                AddWall(MinX, y, zStart, 1, depth);
                AddWall(MinX + RoomWidth - 1, y, zStart, 1, depth);
            }
        }

        public static MapData Generate(int seed, GeneratorSettings settings)
        {
            settings ??= new GeneratorSettings();
            var rng = new System.Random(seed);

            int players = Mathf.Clamp(settings.players, 2, 4);
            int difficulty = Mathf.Clamp(settings.difficulty, 1, 3);
            int requestedRooms = Mathf.Clamp(settings.rooms, 1, 7);

            string mapName = $"Procedural {seed} ({players}J - D{difficulty})";
            var map = new MapData
            {
                version = MapData.FormatVersion,
                name = mapName,
                pieces = new List<MapPiece>()
            };

            var ctx = new BuildContext(map, rng, new GeneratorSettings
            {
                players = players,
                rooms = requestedRooms,
                difficulty = difficulty
            });

            ctx.currentZ = -96;

            // 1. Sala de Inicio (Start Room: 12x8)
            BuildStartRoom(ctx);

            // 2. Selección variada de arquetipos de salas
            List<RoomArchetype> sequence = ChooseRoomSequence(rng, requestedRooms);

            for (int i = 0; i < sequence.Count; i++)
            {
                if (ctx.currentZ > 88) break;

                switch (sequence[i])
                {
                    case RoomArchetype.TwinAirlock:
                        BuildTwinAirlockRoom(ctx, i);
                        break;
                    case RoomArchetype.SideWindowPit:
                        BuildSideWindowPitRoom(ctx, i);
                        break;
                    case RoomArchetype.BlindLedge:
                        BuildBlindLedgeRoom(ctx, i);
                        break;
                    case RoomArchetype.TwinBridges:
                        BuildTwinBridgesRoom(ctx, i);
                        break;
                    case RoomArchetype.BoxBastion:
                        BuildBoxBastionRoom(ctx, i);
                        break;
                    case RoomArchetype.CrossGazeSlalom:
                        BuildCrossGazeSlalomRoom(ctx, i);
                        break;
                }
            }

            // 3. Sala de Meta (Goal Room: 12x8)
            BuildGoalRoom(ctx);

            return map;
        }

        private static List<RoomArchetype> ChooseRoomSequence(System.Random rng, int count)
        {
            var result = new List<RoomArchetype>(count);
            var pool = new List<RoomArchetype>
            {
                RoomArchetype.TwinAirlock,
                RoomArchetype.SideWindowPit,
                RoomArchetype.BlindLedge,
                RoomArchetype.TwinBridges,
                RoomArchetype.BoxBastion,
                RoomArchetype.CrossGazeSlalom
            };

            RoomArchetype last = (RoomArchetype)(-1);
            for (int i = 0; i < count; i++)
            {
                if (i == 0)
                {
                    RoomArchetype first = rng.Next(2) == 0 ? RoomArchetype.TwinAirlock : RoomArchetype.SideWindowPit;
                    result.Add(first);
                    last = first;
                    continue;
                }

                RoomArchetype pick;
                int guard = 0;
                do
                {
                    pick = pool[rng.Next(pool.Count)];
                    guard++;
                } while (pick == last && guard < 10);

                result.Add(pick);
                last = pick;
            }
            return result;
        }

        // =====================================================================
        // SALA INICIAL Y SALA DE META
        // =====================================================================

        private static void BuildStartRoom(BuildContext ctx)
        {
            int z0 = ctx.currentZ;
            int depth = 8;

            ctx.AddFloor(MinX, 0, z0, RoomWidth, depth);
            ctx.AddWall(MinX, 0, z0, RoomWidth, 1);
            ctx.AddSideWalls(z0 + 1, depth - 1, 0);

            ctx.map.pieces.Add(new MapPiece
            {
                type = PieceType.Start,
                x = -1,
                y = 0,
                z = z0 + 2,
                w = 3,
                d = 3,
                rot = 0
            });

            // Ventanas laterales de bienvenida dejando libre el corredor central x = -1..1
            ctx.AddWindow(-4, 0, z0 + 6, 3, 1);
            ctx.AddWindow(2, 0, z0 + 6, 3, 1);

            ctx.currentZ += depth;
        }

        private static void BuildGoalRoom(BuildContext ctx)
        {
            int z0 = ctx.currentZ;
            int depth = 8;

            ctx.AddFloor(MinX, 0, z0, RoomWidth, depth);
            ctx.AddSideWalls(z0, depth, 0);
            ctx.AddWall(MinX, 0, z0 + depth - 1, RoomWidth, 1);

            // Podio de meta a 0.5m (nivel 1)
            ctx.AddFloor(-3, 1, z0 + 3, 6, 4);

            ctx.map.pieces.Add(new MapPiece
            {
                type = PieceType.Goal,
                x = -1,
                y = 1,
                z = z0 + 4,
                w = 3,
                d = 3,
                rot = 0
            });

            ctx.currentZ += depth;
        }

        // =====================================================================
        // ARQUETIPO 1: TwinAirlock (Esclusa con Muro de Oclusión y Ventanas)
        // =====================================================================
        private static void BuildTwinAirlockRoom(BuildContext ctx, int roomIndex)
        {
            int z0 = ctx.currentZ;
            int depth = 14;
            int ch = ctx.AllocateChannel();
            bool mirror = ctx.rng.Next(2) == 0;

            ctx.AddFloor(MinX, 0, z0, RoomWidth, depth);
            ctx.AddSideWalls(z0, depth, 0);

            if (roomIndex > 0)
                ctx.AddCheckpoint(-1, 0, z0 + 1);

            // Pared divisoria principal en z0 + 6 con puerta en (0, 0, wallZ)
            int wallZ = z0 + 6;
            ctx.AddWall(-5, 0, wallZ, 1, 1);
            ctx.AddWindow(-4, 0, wallZ, 2, 1);
            ctx.AddWall(-2, 0, wallZ, 2, 1);

            ctx.AddDoor(0, 0, wallZ, 0, ch);

            ctx.AddWall(1, 0, wallZ, 1, 1);
            ctx.AddWindow(2, 0, wallZ, 2, 1);
            ctx.AddWall(4, 0, wallZ, 1, 1);

            // Placa A (antes de la puerta): libre de paredes en (-4..-3 o 2..3, z0+3..z0+4)
            int plateAX = mirror ? 2 : -4;
            ctx.AddPlate(plateAX, 0, z0 + 3, ch);

            // Caja de asistencia en el lado de entrada
            int boxX = mirror ? -4 : 3;
            if (ctx.difficulty >= 2)
            {
                ctx.AddBlock(boxX - 1, 0, z0 + 3, 2, 1);
                ctx.AddBox(boxX, 2, z0 + 3);
            }
            else
            {
                ctx.AddBox(boxX, 0, z0 + 4);
            }

            // Muro de oclusión frontal 2 celdas detrás de la puerta (z0 + 8):
            // Deja libre (0, wallZ+1) para el paso de la puerta, pero corta la visión recta desde atrás.
            ctx.AddWall(-2, 0, wallZ + 2, 4, 1);

            // Placa B de retorno pasando la puerta (en pedestal de 1m en dificultad 2-3)
            int plateBX = mirror ? -4 : 2;
            if (ctx.difficulty >= 2)
            {
                ctx.AddBlock(plateBX, 0, wallZ + 3, 3, 3);
                ctx.AddPlate(plateBX, 2, wallZ + 3, ch);
            }
            else
            {
                ctx.AddPlate(plateBX, 0, wallZ + 3, ch);
            }

            if (ctx.players >= 3)
            {
                int balcX = mirror ? -4 : 2;
                ctx.AddBlock(balcX, 0, z0 + 1, 2, 2);
            }

            ctx.currentZ += depth;
        }

        // =====================================================================
        // ARQUETIPO 2: SideWindowPit (Foso en Zig-Zag con Galería de Ventanas)
        // =====================================================================
        private static void BuildSideWindowPitRoom(BuildContext ctx, int roomIndex)
        {
            int z0 = ctx.currentZ;
            int depth = 16;
            int ch = ctx.AllocateChannel();
            bool corridorOnRight = ctx.rng.Next(2) == 0;

            // Lobby de entrada (z0..z0+3) y lobby de salida (z0+12..z0+15, 4m de profundo)
            ctx.AddFloor(MinX, 0, z0, RoomWidth, 4);
            ctx.AddFloor(MinX, 0, z0 + depth - 4, RoomWidth, 4);
            ctx.AddSideWalls(z0, depth, 0);

            if (roomIndex > 0)
                ctx.AddCheckpoint(-1, 0, z0 + 1);

            int pitZStart = z0 + 4;
            int pitLength = depth - 8; // 8 celdas de foso (z0+4 .. z0+11)
            int doorZ = z0 + depth - 4; // z0 + 12 (tiene piso en z0+11 por galería y en z0+12..15 por salida)

            if (corridorOnRight)
            {
                // Galería del observador a la derecha: x = 2..4 (ancho 3)
                ctx.AddFloor(2, 0, pitZStart, 3, pitLength);
                // Tabique con ventanas en x = 2 durante el foso (z0+4 .. z0+11)
                BuildWindowedPartition(ctx, 2, 0, pitZStart, pitLength);

                // Puerta al final de la galería en (3, 0, doorZ), con piso libre en (3, doorZ-1) y (3, doorZ+1)
                ctx.AddWall(2, 0, doorZ, 1, 1);
                ctx.AddDoor(3, 0, doorZ, 0, ch);
                ctx.AddWall(4, 0, doorZ, 1, 1);

                // Muro deflector al inicio del foso
                ctx.AddWall(-3, 0, z0 + 3, 4, 1);

                // Piedras en el foso (x entre -4 y -1)
                BuildSteppingStones(ctx, -4, -1, pitZStart, pitLength);

                // Placa principal al otro lado del foso (x = -4..-3, z = doorZ+1..doorZ+2) y placa de apoyo antes de la puerta
                ctx.AddPlate(-4, 0, doorZ + 1, ch);
            }
            else
            {
                // Galería del observador a la izquierda: x = -5..-3 (ancho 3)
                ctx.AddFloor(-5, 0, pitZStart, 3, pitLength);
                BuildWindowedPartition(ctx, -3, 0, pitZStart, pitLength);

                // Puerta al final de la galería en (-4, 0, doorZ), con piso libre en (-4, doorZ-1) y (-4, doorZ+1)
                ctx.AddWall(-5, 0, doorZ, 1, 1);
                ctx.AddDoor(-4, 0, doorZ, 0, ch);
                ctx.AddWall(-3, 0, doorZ, 1, 1);

                // Muro deflector al inicio del foso
                ctx.AddWall(-1, 0, z0 + 3, 4, 1);

                // Piedras en el foso (x entre -1 y 2)
                BuildSteppingStones(ctx, -1, 2, pitZStart, pitLength);

                // Placa al otro lado del foso (x = 2..3, z = doorZ+1..doorZ+2)
                ctx.AddPlate(2, 0, doorZ + 1, ch);
            }

            ctx.currentZ += depth;
        }

        private static void BuildWindowedPartition(BuildContext ctx, int x, int y, int zStart, int length)
        {
            for (int dz = 0; dz < length; dz++)
            {
                bool isPillar;
                if (ctx.difficulty == 1)
                    isPillar = (dz == 0 || dz == length - 1 || dz == length / 2);
                else if (ctx.difficulty == 2)
                    isPillar = (dz % 3 == 0);
                else
                    isPillar = (dz % 2 == 0);

                if (isPillar)
                    ctx.AddWall(x, y, zStart + dz, 1, 1);
                else
                    ctx.AddWindow(x, y, zStart + dz, 1, 1);
            }
        }

        /// <summary>
        /// Coloca 3 islas de piedra (Stone 2x2) en zig-zag dentro de un foso de 8m (pitLength = 8).
        /// Entrada desde zStart-1; piedras en zStart+1..2, zStart+3..4, zStart+6..7; salida en zStart+8.
        /// Huecos máximos de 1 a 2 celdas vacías: saltos 100% fiables con cámara lateral.
        /// </summary>
        private static void BuildSteppingStones(BuildContext ctx, int xLeft, int xRight, int zStart, int pitLength)
        {
            int stoneW = 2;
            int stoneD = 2;
            int[] offsetsZ = { 1, 3, 6 };
            bool useLeft = ctx.rng.Next(2) == 0;

            for (int i = 0; i < offsetsZ.Length; i++)
            {
                int sz = zStart + offsetsZ[i];
                if (sz + stoneD > zStart + pitLength)
                    sz = zStart + pitLength - stoneD;

                int sx = useLeft ? xLeft : xRight;
                useLeft = !useLeft;

                int sy = 0;
                if (ctx.difficulty >= 2 && i == 1)
                    sy = 1; // +0.5m en la piedra central

                ctx.AddFloor(sx, sy, sz, stoneW, stoneD, PieceType.Stone);
            }
        }

        // =====================================================================
        // ARQUETIPO 3: BlindLedge (La Cornisa de 2m con Pretil y Placa de Rescate)
        // =====================================================================
        private static void BuildBlindLedgeRoom(BuildContext ctx, int roomIndex)
        {
            int z0 = ctx.currentZ;
            int depth = 16;
            int ch = ctx.AllocateChannel();
            bool towerOnLeft = ctx.rng.Next(2) == 0;

            ctx.AddFloor(MinX, 0, z0, RoomWidth, depth);
            ctx.AddSideWalls(z0, depth, 0);

            if (roomIndex > 0)
                ctx.AddCheckpoint(-1, 0, z0 + 1);

            // Cornisa alta de 2m (4 niveles = 2 capas de Block) en z0 + 6 .. z0 + 10 (5m de profundo)
            // Deja libre z0 + 11 para que la puerta en z0 + 12 tenga piso limpio adelante.
            int ledgeZ = z0 + 6;
            int ledgeD = 5;
            int ledgeX = towerOnLeft ? -1 : -5;
            int ledgeW = 6;

            ctx.AddPlatformBlocks(ledgeX, 0, ledgeZ, ledgeW, ledgeD, 2);

            // Pretil (Block en nivel 4 -> 3m de alto) en el frente de la cornisa, salvo 2 celdas de subida
            int climbGapOffset = towerOnLeft ? 0 : (ledgeW - 2);
            for (int dx = 0; dx < ledgeW; dx++)
            {
                if (dx >= climbGapOffset && dx < climbGapOffset + 2)
                    continue;
                ctx.AddBlock(ledgeX + dx, 4, ledgeZ, 1, 1);
            }

            // Placa A arriba de la cornisa (nivel 4 = 2.0m), oculta detrás del pretil
            int plateX = ledgeX + (ledgeW / 2) - 1;
            ctx.AddPlate(plateX, 4, ledgeZ + 2, ch);

            // Cajas en planta baja para armar el escalón a la cornisa
            int boxSpawnX = towerOnLeft ? 2 : -3;
            ctx.AddBox(boxSpawnX, 0, z0 + 4);
            ctx.AddBox(0, 0, z0 + 4);

            // Torre / Mirador del observador del lado opuesto (termina en z0 + 9 para dejar libre z0 + 10..11)
            int towerX = towerOnLeft ? -5 : 2;
            ctx.AddPlatformBlocks(towerX, 0, z0 + 4, 3, 2, 1); // escalón 1m (z0+4..5)
            ctx.AddPlatformBlocks(towerX, 0, z0 + 6, 3, 4, 2); // mirador 2m (z0+6..9)

            // Pared de cierre al fondo (z0 + 12) con Puerta en doorX.
            // Adelante de la puerta (doorX, z0+11) y atrás (doorX, z0+13) hay piso nivel 0 libre.
            int exitWallZ = z0 + 12;
            int doorX = towerOnLeft ? -3 : 2;
            for (int x = MinX + 1; x < MinX + RoomWidth - 1; x++)
            {
                if (x == doorX)
                    ctx.AddDoor(x, 0, exitWallZ, 0, ch);
                else if (Mathf.Abs(x - doorX) == 2)
                    ctx.AddWindow(x, 0, exitWallZ, 1, 1);
                else
                    ctx.AddWall(x, 0, exitWallZ, 1, 1);
            }

            // Placa B de rescate del otro lado de la puerta (en z0 + 13), detrás de un pequeño muro ciego,
            // para que el primero que cruza mantenga abierta la puerta y baje el compañero de la cornisa.
            int rescuePlateX = towerOnLeft ? 1 : -3;
            ctx.AddPlate(rescuePlateX, 0, exitWallZ + 1, ch);

            ctx.currentZ += depth;
        }

        // =====================================================================
        // ARQUETIPO 4: TwinBridges (Puentes Paralelos con Tabique de Ventanas)
        // =====================================================================
        private static void BuildTwinBridgesRoom(BuildContext ctx, int roomIndex)
        {
            int z0 = ctx.currentZ;
            int depth = 16;
            int chA = ctx.AllocateChannel();
            int chB = ctx.AllocateChannel();

            // Piso de entrada (z0..z0+3, 4m) y piso de salida (z0+12..z0+15, 4m).
            // Así z0+12 es un vestíbulo continuo de piso firme delante de la puerta final en z0+13.
            ctx.AddFloor(MinX, 0, z0, RoomWidth, 4);
            ctx.AddFloor(MinX, 0, z0 + depth - 4, RoomWidth, 4);
            ctx.AddSideWalls(z0, depth, 0);

            if (roomIndex > 0)
                ctx.AddCheckpoint(-1, 0, z0 + 1);

            int pitZ = z0 + 4;
            int pitLen = depth - 8; // 8m de foso (z0+4 .. z0+11). Termina en z0+11, dejando z0+12 libre.

            // Espina central en x = -1..0 durante el foso (z0+4 .. z0+11) con tabique de ventanas
            ctx.AddFloor(-1, 0, pitZ, 2, pitLen);
            BuildWindowedPartition(ctx, -1, 0, pitZ, pitLen);
            BuildWindowedPartition(ctx, 0, 0, pitZ, pitLen);

            // Deflectores en la entrada de cada carril
            ctx.AddWall(-4, 0, z0 + 3, 2, 1);
            ctx.AddWall(2, 0, z0 + 3, 2, 1);

            // Carril Izquierdo (x = -5..-2):
            // Isla 1 (z0+5..z0+7) con Placa A; Isla 2 (z0+9..z0+10) que salta al piso firme en z0+12.
            ctx.AddFloor(-4, 0, pitZ + 1, 2, 3, PieceType.Stone);
            ctx.AddPlate(-4, 0, pitZ + 2, chA);
            ctx.AddFloor(-4, ctx.difficulty >= 2 ? 1 : 0, pitZ + 5, 2, 2, PieceType.Stone);

            // Carril Derecho (x = 1..4):
            // Piedra inicial en z0+5..z0+6 y puente continuo desde z0+7 hasta z0+11 (conecta directo con z0+12).
            ctx.AddFloor(2, 0, pitZ + 1, 2, 2, PieceType.Stone);
            ctx.AddFloor(1, 0, pitZ + 3, 4, 5, PieceType.Floor); // cubre z0+7..z0+11

            // Cerramiento en el carril derecho en z0+8 (pitZ+4) con Puerta A en x=2:
            // Tiene piso en (2, z0+7) y en (2, z0+9).
            int midDoorZ = pitZ + 4;
            ctx.AddWall(1, 0, midDoorZ, 1, 1);
            ctx.AddDoor(2, 0, midDoorZ, 0, chA);
            ctx.AddWall(3, 0, midDoorZ, 2, 1);

            // Pasando la Puerta A está la Placa B en (2..3, z0+9..z0+10) sobre el puente derecho
            ctx.AddPlate(2, 0, pitZ + 5, chB);
            // Y segunda Placa A de retorno en (1..2, z0+11..z0+12) para que el del carril izquierdo no necesite quedarse atrás
            // (Claude: decía AddPlate(-3, 0, z0 + 12): la mitad caía sobre la pared final de z0+13.
            //  Se ubica donde dice el comentario de arriba.)
            ctx.AddPlate(1, 0, z0 + 11, chA);

            ctx.AddBox(0, 0, z0 + 2);

            // Pared final en z0 + 13 (depth - 3) con Puerta B en (0, 0, z0+13).
            // Adelante tiene (0, z0+12) con piso firme y libre; atrás tiene (0, z0+14) con piso firme y libre.
            int endWallZ = z0 + depth - 3;
            for (int x = MinX + 1; x < MinX + RoomWidth - 1; x++)
            {
                if (x == 0)
                    ctx.AddDoor(x, 0, endWallZ, 0, chB);
                else if (x == -2 || x == 2)
                    ctx.AddWindow(x, 0, endWallZ, 1, 1);
                else
                    ctx.AddWall(x, 0, endWallZ, 1, 1);
            }

            // Placa B de rescate pasando la puerta final en (2..3, z0+14..z0+15)
            ctx.AddPlate(2, 0, endWallZ + 1, chB);

            ctx.currentZ += depth;
        }

        // =====================================================================
        // ARQUETIPO 5: BoxBastion (Laberinto Ciego de Caja + Pasarela Mirador)
        // =====================================================================
        private static void BuildBoxBastionRoom(BuildContext ctx, int roomIndex)
        {
            int z0 = ctx.currentZ;
            int depth = 16;
            int ch = ctx.AllocateChannel();
            bool catwalkOnRight = ctx.rng.Next(2) == 0;

            ctx.AddFloor(MinX, 0, z0, RoomWidth, depth);
            ctx.AddSideWalls(z0, depth, 0);

            if (roomIndex > 0)
                ctx.AddCheckpoint(-1, 0, z0 + 1);

            if (catwalkOnRight)
            {
                // Pasarela elevada (+1m = nivel 2) a la derecha (x = 2..4) desde z0+3 hasta z0+10 (8m)
                ctx.AddPlatformBlocks(2, 0, z0 + 3, 3, 8, 1);
                BuildWindowedPartition(ctx, 1, 0, z0 + 3, 8);

                // Laberinto ciego en el lado izquierdo (x = -5..0)
                ctx.AddWall(-5, 0, z0 + 4, 4, 1);
                ctx.AddWall(-3, 0, z0 + 8, 4, 1);

                // Caja en el laberinto y Placa A sobre un pedestal de 1m (nivel 2) en (-4..-3, z0+9..z0+10)
                ctx.AddBox(-4, 0, z0 + 6);
                ctx.AddBlock(-4, 0, z0 + 9, 2, 2);
                ctx.AddPlate(-4, 2, z0 + 9, ch);
            }
            else
            {
                // Pasarela elevada a la izquierda (x = -5..-3) desde z0+3 hasta z0+10
                ctx.AddPlatformBlocks(-5, 0, z0 + 3, 3, 8, 1);
                BuildWindowedPartition(ctx, -2, 0, z0 + 3, 8);

                // Laberinto ciego a la derecha (x = -1..4)
                ctx.AddWall(1, 0, z0 + 4, 4, 1);
                ctx.AddWall(-1, 0, z0 + 8, 4, 1);

                ctx.AddBox(3, 0, z0 + 6);
                ctx.AddBlock(2, 0, z0 + 9, 2, 2);
                ctx.AddPlate(2, 2, z0 + 9, ch);
            }

            // Cierre al fondo de la sala en z0 + 12 con puerta central en (0, 0, z0+12).
            // Tanto (0, z0+11) como (0, z0+13) tienen piso nivel 0 completamente despejado.
            int exitZ = z0 + 12;
            for (int x = MinX + 1; x < MinX + RoomWidth - 1; x++)
            {
                if (x == 0)
                    ctx.AddDoor(x, 0, exitZ, 0, ch);
                else if (x == -2 || x == 2)
                    ctx.AddWindow(x, 0, exitZ, 1, 1);
                else
                    ctx.AddWall(x, 0, exitZ, 1, 1);
            }

            // Placa B de rescate pasando la puerta (en z0+13..z0+14) para liberar al compañero del pedestal
            int rescueX = catwalkOnRight ? 2 : -4;
            ctx.AddPlate(rescueX, 0, exitZ + 1, ch);

            ctx.currentZ += depth;
        }

        // =====================================================================
        // ARQUETIPO 6: CrossGazeSlalom (Balcones Laterales y Foso Central)
        // =====================================================================
        private static void BuildCrossGazeSlalomRoom(BuildContext ctx, int roomIndex)
        {
            int z0 = ctx.currentZ;
            int depth = 16;
            int ch = ctx.AllocateChannel();

            // Piso de entrada (z0..z0+3, 4m) y piso de salida (z0+12..z0+15, 4m)
            // Deja z0+12 como corredor de piso firme frente a la puerta de salida en z0+13.
            ctx.AddFloor(MinX, 0, z0, RoomWidth, 4);
            ctx.AddFloor(MinX, 0, z0 + depth - 4, RoomWidth, 4);
            ctx.AddSideWalls(z0, depth, 0);

            if (roomIndex > 0)
                ctx.AddCheckpoint(-1, 0, z0 + 1);

            int midZ = z0 + 4;
            int midLen = depth - 8; // 8m de foso (z0+4 .. z0+11)

            // Balcón izquierdo de 3m de ancho (x = -5..-3) con tabique de ventanas en x = -3,
            // dejando x = -5..-4 (2m de ancho) 100% libres para caminar y alojar la Placa 2x2 en (-5, midZ+1).
            ctx.AddFloor(-5, 0, midZ, 3, midLen);
            BuildWindowedPartition(ctx, -3, 0, midZ, midLen);

            // Balcón derecho en x = 2..4 con tabique de ventanas en x = 2 (deja x = 3..4 libres)
            ctx.AddFloor(2, 0, midZ, 3, midLen);
            BuildWindowedPartition(ctx, 2, 0, midZ, midLen);

            // Corte en ambos balcones en midZ + 5 (z0 + 9) para obligar a cruzar por las piedras del foso central
            ctx.AddWall(-5, 0, midZ + 5, 2, 1);
            ctx.AddWall(3, 0, midZ + 5, 2, 1);

            // Muro deflector central en la entrada (x = -2..1, z = z0 + 3)
            ctx.AddWall(-2, 0, z0 + 3, 4, 1);

            // Islas de piedra en la fosa central (x = -2..1) en zig-zag (en z0+5..6, z0+7..8 y z0+10..11)
            // La tercera piedra en (-1..0, z0+10..11) conecta directo al frente con el piso de salida en z0+12.
            ctx.AddFloor(-2, 0, midZ + 1, 2, 2, PieceType.Stone);
            ctx.AddFloor(0, ctx.difficulty >= 2 ? 1 : 0, midZ + 3, 2, 2, PieceType.Stone);
            ctx.AddFloor(-1, 0, midZ + 6, 2, 2, PieceType.Stone);

            // Placa A en el balcón izquierdo: en x = -5..-4, z = midZ+1..midZ+2 (100% sobre piso y sin tocar x=-3)
            ctx.AddPlate(-5, 0, midZ + 1, ch);
            ctx.AddBox(-4, 0, z0 + 2);

            // Placa B del otro lado del foso central (en x = 2..3, z = z0+12..z0+13) antes de la puerta en z0+13,
            // o pasando la puerta, para que el que cruza el foso le abra al del balcón izquierdo.
            int doorZ = z0 + depth - 3; // z0 + 13 (tiene piso libre en z0+12 y en z0+14)
            // (Claude: decía AddPlate(-4, 0, z0 + 12): la mitad caía sobre la pared de la puerta en z0+13.
            //  Va pasando la puerta, como dice el comentario: así el que cruzó le abre al del balcón.)
            ctx.AddPlate(-4, 0, doorZ + 1, ch);

            for (int x = MinX + 1; x < MinX + RoomWidth - 1; x++)
            {
                if (x == 0)
                    ctx.AddDoor(x, 0, doorZ, 0, ch);
                else if (x == -2 || x == 2)
                    ctx.AddWindow(x, 0, doorZ, 1, 1);
                else
                    ctx.AddWall(x, 0, doorZ, 1, 1);
            }

            ctx.currentZ += depth;
        }

        // =====================================================================
        // VALIDACIÓN ESTRUCTURAL Y GEOMÉTRICA (Validate)
        // =====================================================================

        /// <summary>
        /// Chequeos estructurales y geométricos: devuelve la lista de problemas (vacía = OK).
        /// Verifica:
        /// 1. Un solo Start y al menos un Goal dentro de límites.
        /// 2. <= 31 placas y toda Door tiene >= 1 Plate de su canal.
        /// 3. Toda Plate (2x2) tiene piso caminable bajo sus 4 celdas y ninguna de sus 4 celdas choca con Wall/Window.
        /// 4. Toda Door tiene piso caminable en su celda y en sus 2 celdas de paso, sin bloqueo de Wall/Window.
        /// 5. Flood-fill de piso/salto desde Start hasta Goal respetando bloqueos de Wall/Window.
        /// </summary>
        public static List<string> Validate(MapData map)
        {
            var issues = new List<string>();
            if (map == null)
            {
                issues.Add("El objeto MapData es nulo.");
                return issues;
            }
            if (map.pieces == null || map.pieces.Count == 0)
            {
                issues.Add("El mapa no contiene piezas.");
                return issues;
            }

            int startCount = 0;
            int goalCount = 0;
            int plateCount = 0;
            int[] platesByChannel = new int[4];
            var doors = new List<MapPiece>();
            var plates = new List<MapPiece>();
            MapPiece startPiece = null;
            var goalPieces = new List<MapPiece>();

            // Celdas con superficie caminable (x, z) -> lista de alturas de cara superior (niveles)
            var surfaceLevels = new Dictionary<Vector2Int, List<int>>();
            // Celdas bloqueadas para el paso peatonal por Wall o Window
            var blockedCells = new HashSet<Vector2Int>();

            for (int i = 0; i < map.pieces.Count; i++)
            {
                var p = map.pieces[i];
                if (p == null)
                {
                    issues.Add($"La pieza en el índice {i} es nula.");
                    continue;
                }

                Vector2Int fp = p.Footprint;
                if (fp.x <= 0 || fp.y <= 0)
                    issues.Add($"Pieza {p.type} en ({p.x},{p.y},{p.z}) tiene dimensiones inválidas ({p.w}x{p.d}).");

                // 1. Chequeo de límites duros
                if (p.x < -MapData.MaxCoordinate || p.x + fp.x > MapData.MaxCoordinate ||
                    p.z < -MapData.MaxCoordinate || p.z + fp.y > MapData.MaxCoordinate)
                {
                    issues.Add($"Pieza {p.type} en ({p.x},{p.z}) con tamaño ({fp.x}x{fp.y}) excede MaxCoordinate (±{MapData.MaxCoordinate}).");
                }

                if (p.y < 0 || p.y > MapData.MaxLevel)
                {
                    issues.Add($"Pieza {p.type} en ({p.x},{p.y},{p.z}) tiene nivel y={p.y} fuera de [0..{MapData.MaxLevel}].");
                }

                // 2. Clasificación de piezas
                switch (p.type)
                {
                    case PieceType.Start:
                        startCount++;
                        startPiece = p;
                        break;
                    case PieceType.Goal:
                        goalCount++;
                        goalPieces.Add(p);
                        break;
                    case PieceType.Plate:
                        plateCount++;
                        plates.Add(p);
                        if (p.channel < 0 || p.channel > 3)
                            issues.Add($"Plate en ({p.x},{p.z}) tiene canal inválido {p.channel} (debe ser 0..3).");
                        else
                            platesByChannel[p.channel]++;
                        break;
                    case PieceType.Door:
                        doors.Add(p);
                        if (p.channel < 0 || p.channel > 3)
                            issues.Add($"Door en ({p.x},{p.z}) tiene canal inválido {p.channel} (debe ser 0..3).");
                        break;
                    case PieceType.Wall:
                    case PieceType.Window:
                        for (int cx = p.x; cx < p.x + fp.x; cx++)
                        {
                            for (int cz = p.z; cz < p.z + fp.y; cz++)
                            {
                                blockedCells.Add(new Vector2Int(cx, cz));
                            }
                        }
                        break;
                }

                // 3. Registro de superficies caminables (Floor, Stone, Block)
                if (p.type == PieceType.Floor || p.type == PieceType.Stone || p.type == PieceType.Block)
                {
                    int topLevel = p.type == PieceType.Block ? p.y + 2 : p.y;
                    for (int cx = p.x; cx < p.x + fp.x; cx++)
                    {
                        for (int cz = p.z; cz < p.z + fp.y; cz++)
                        {
                            var cell = new Vector2Int(cx, cz);
                            if (!surfaceLevels.TryGetValue(cell, out var list))
                            {
                                list = new List<int>(2);
                                surfaceLevels[cell] = list;
                            }
                            if (!list.Contains(topLevel))
                                list.Add(topLevel);
                        }
                    }
                }
            }

            if (startCount != 1)
                issues.Add($"El mapa debe tener exactamente 1 Start (encontrados: {startCount}).");

            if (goalCount < 1)
                issues.Add("El mapa debe tener al menos 1 Goal.");

            if (plateCount > MaxPlatesAllowed)
                issues.Add($"El mapa tiene {plateCount} placas, superando el máximo permitido de {MaxPlatesAllowed}.");

            // 4. Verificación geométrica de cada Plate (sus 4 celdas deben tener soporte y no chocar con Wall/Window)
            foreach (var pl in plates)
            {
                Vector2Int fp = pl.Footprint;
                for (int cx = pl.x; cx < pl.x + fp.x; cx++)
                {
                    for (int cz = pl.z; cz < pl.z + fp.y; cz++)
                    {
                        var c = new Vector2Int(cx, cz);
                        if (blockedCells.Contains(c))
                        {
                            issues.Add($"La Plate en ({pl.x},{pl.y},{pl.z}) tiene su celda ({cx},{cz}) superpuesta con Wall/Window.");
                        }
                        if (!surfaceLevels.TryGetValue(c, out var levels) || !levels.Contains(pl.y))
                        {
                            issues.Add($"La Plate en ({pl.x},{pl.y},{pl.z}) no tiene superficie de apoyo de nivel {pl.y} bajo su celda ({cx},{cz}).");
                        }
                    }
                }
            }

            // 5. Verificación geométrica de cada Door (canal válido, piso propio y las 2 celdas de paso libres de Wall/Window y con piso)
            foreach (var d in doors)
            {
                if (d.channel >= 0 && d.channel <= 3 && platesByChannel[d.channel] == 0)
                {
                    issues.Add($"La puerta en ({d.x},{d.z}) usa el canal {d.channel}, pero no existe ninguna Plate con ese canal.");
                }

                var doorCell = new Vector2Int(d.x, d.z);
                if (blockedCells.Contains(doorCell))
                {
                    issues.Add($"La puerta en ({d.x},{d.z}) está superpuesta en la misma celda con Wall/Window.");
                }
                if (!surfaceLevels.ContainsKey(doorCell))
                {
                    issues.Add($"La puerta en ({d.x},{d.z}) no tiene piso debajo.");
                }

                // Si rot es par (0 o 2), la losa corre a lo largo de X y se atraviesa en Z: paso en (x, z-1) y (x, z+1).
                // Si rot es impar (1 o 3), la losa corre a lo largo de Z y se atraviesa en X: paso en (x-1, z) y (x+1, z).
                Vector2Int stepA = (d.rot % 2 == 0) ? new Vector2Int(d.x, d.z - 1) : new Vector2Int(d.x - 1, d.z);
                Vector2Int stepB = (d.rot % 2 == 0) ? new Vector2Int(d.x, d.z + 1) : new Vector2Int(d.x + 1, d.z);

                if (blockedCells.Contains(stepA) || !surfaceLevels.ContainsKey(stepA))
                {
                    issues.Add($"La puerta en ({d.x},{d.z}) tiene bloqueada o sin piso su celda de acceso ({stepA.x},{stepA.y}).");
                }
                if (blockedCells.Contains(stepB) || !surfaceLevels.ContainsKey(stepB))
                {
                    issues.Add($"La puerta en ({d.x},{d.z}) tiene bloqueada o sin piso su celda de acceso ({stepB.x},{stepB.y}).");
                }
            }

            // Construimos el diccionario de celdas realmente transitables (tienen piso y NO tienen Wall/Window)
            var walkableCells = new HashSet<Vector2Int>();
            foreach (var kvp in surfaceLevels)
            {
                if (!blockedCells.Contains(kvp.Key))
                    walkableCells.Add(kvp.Key);
            }

            // 6. Verificación de conectividad desde Start hasta cada Goal respetando paredes
            if (startPiece != null && goalPieces.Count > 0)
            {
                var startCenter = new Vector2Int(startPiece.x + 1, startPiece.z + 1);
                if (!walkableCells.Contains(startCenter))
                {
                    issues.Add($"El Start en ({startPiece.x},{startPiece.z}) no tiene piso libre de paredes.");
                }
                else
                {
                    var visited = FloodFillReachableCells(startCenter, walkableCells, blockedCells);
                    foreach (var g in goalPieces)
                    {
                        var goalCenter = new Vector2Int(g.x + 1, g.z + 1);
                        if (!walkableCells.Contains(goalCenter))
                        {
                            issues.Add($"El Goal en ({g.x},{g.z}) no tiene piso libre de paredes.");
                        }
                        else if (!visited.Contains(goalCenter))
                        {
                            issues.Add($"No hay ruta conectada libre de paredes entre el Start y el Goal en ({g.x},{g.z}).");
                        }
                    }
                }
            }

            return issues;
        }

        private static HashSet<Vector2Int> FloodFillReachableCells(
            Vector2Int origin,
            HashSet<Vector2Int> walkableCells,
            HashSet<Vector2Int> blockedCells)
        {
            var visited = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();

            visited.Add(origin);
            queue.Enqueue(origin);

            const int MaxJumpSpan = 4;

            while (queue.Count > 0)
            {
                Vector2Int curr = queue.Dequeue();

                for (int dx = -MaxJumpSpan; dx <= MaxJumpSpan; dx++)
                {
                    for (int dz = -MaxJumpSpan; dz <= MaxJumpSpan; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        if (dx * dx + dz * dz > MaxJumpSpan * MaxJumpSpan) continue;

                        var next = new Vector2Int(curr.x + dx, curr.y + dz);
                        if (visited.Contains(next) || !walkableCells.Contains(next))
                            continue;

                        // Verificamos que el segmento entre curr y next no atraviese una pared sólida (Wall/Window)
                        if (LineHitsBlockedCell(curr, next, blockedCells))
                            continue;

                        visited.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }

            return visited;
        }

        private static bool LineHitsBlockedCell(Vector2Int a, Vector2Int b, HashSet<Vector2Int> blockedCells)
        {
            int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            if (steps <= 1) return false;

            for (int s = 1; s < steps; s++)
            {
                float t = s / (float)steps;
                int ix = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t));
                int iz = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
                var mid = new Vector2Int(ix, iz);
                if (mid != a && mid != b && blockedCells.Contains(mid))
                    return true;
            }
            return false;
        }
    }
}