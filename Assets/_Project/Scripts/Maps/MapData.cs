using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace PointOfYou
{
    public enum PieceType { Floor, Wall, Window, Block, Stone, Plate, Door, Box, Start, Checkpoint, Goal }

    /// <summary>
    /// Una pieza del mapa. La grilla es de 1 m en X/Z; la altura va en niveles de 0,5 m
    /// (así un bloque de 1 m son 2 niveles y las medidas de salto del prototipo siguen valiendo).
    /// </summary>
    [Serializable]
    public class MapPiece
    {
        public PieceType type;
        public int x, y, z;      // celda de la esquina mínima y nivel de altura
        public int w = 1, d = 1; // tamaño en celdas (sin rotar)
        public int rot;          // 0..3, de a 90°
        public int channel;      // placas y puertas con el mismo canal van juntas

        public MapPiece Copy() => (MapPiece)MemberwiseClone();

        /// <summary>Tamaño ocupado en la grilla, ya rotado.</summary>
        public Vector2Int Footprint => rot % 2 == 0 ? new Vector2Int(w, d) : new Vector2Int(d, w);

        public bool Covers(int cx, int cz)
        {
            var f = Footprint;
            return cx >= x && cx < x + f.x && cz >= z && cz < z + f.y;
        }

        public float BaseHeight => y * MapData.LevelHeight;
    }

    /// <summary>Un mapa hecho con el editor: lista de piezas + nombre. Se guarda como JSON y se comparte como código.</summary>
    [Serializable]
    public class MapData
    {
        public const int FormatVersion = 1;
        public const float LevelHeight = 0.5f;
        public const int MaxLevel = 24;
        public const int MaxCoordinate = 120;
        const string CodePrefix = "POY1:";

        public int version = FormatVersion;
        public string name = "Mapa nuevo";
        public List<MapPiece> pieces = new();

        public string ToJson() => JsonUtility.ToJson(this);

        public static MapData FromJson(string json)
        {
            var map = JsonUtility.FromJson<MapData>(json);
            if (map == null) throw new FormatException("JSON de mapa vacío");
            map.pieces ??= new List<MapPiece>();
            if (string.IsNullOrWhiteSpace(map.name)) map.name = "Mapa sin nombre";
            return map;
        }

        public MapData Clone() => FromJson(ToJson());

        /// <summary>Código para compartir por chat: JSON comprimido en base64, con prefijo de versión.</summary>
        public string ToCode()
        {
            byte[] raw = Encoding.UTF8.GetBytes(ToJson());
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                gzip.Write(raw, 0, raw.Length);
            return CodePrefix + Convert.ToBase64String(output.ToArray());
        }

        public static bool TryFromCode(string code, out MapData map, out string error)
        {
            map = null;
            error = null;
            code = (code ?? "").Trim();
            if (!code.StartsWith(CodePrefix))
            {
                error = "Eso no es un código de mapa de Point of You (tiene que empezar con POY1:).";
                return false;
            }
            try
            {
                byte[] compressed = Convert.FromBase64String(code.Substring(CodePrefix.Length));
                using var input = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
                using var reader = new StreamReader(input, Encoding.UTF8);
                map = FromJson(reader.ReadToEnd());
                return true;
            }
            catch (Exception e)
            {
                error = $"El código está incompleto o dañado ({e.GetType().Name}).";
                return false;
            }
        }

        public MapPiece FindFirst(PieceType type) => pieces.Find(p => p.type == type);

        /// <summary>Mapa inicial del editor: un piso de 12x12 con el inicio y la meta.</summary>
        public static MapData CreateDefault(string mapName = "Mapa nuevo")
        {
            var map = new MapData { name = mapName };
            map.pieces.Add(new MapPiece { type = PieceType.Floor, x = -6, z = -2, w = 12, d = 16 });
            map.pieces.Add(new MapPiece { type = PieceType.Start, x = -1, z = -1, w = 3, d = 3 });
            map.pieces.Add(new MapPiece { type = PieceType.Goal, x = -1, z = 10, w = 3, d = 3 });
            return map;
        }
    }

    /// <summary>Mapas guardados en esta PC (carpeta "Mapas" dentro de persistentDataPath).</summary>
    public static class MapLibrary
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "Mapas");

        public static List<string> List()
        {
            var names = new List<string>();
            if (!Directory.Exists(Folder)) return names;
            var files = new DirectoryInfo(Folder).GetFiles("*.json");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            foreach (var f in files) names.Add(Path.GetFileNameWithoutExtension(f.Name));
            return names;
        }

        public static string FileNameFor(string mapName)
        {
            var sb = new StringBuilder();
            foreach (char c in (mapName ?? "").Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim('.', ' ');
            return string.IsNullOrEmpty(s) ? "Mapa" : s;
        }

        /// <summary>Guarda (pisa si ya existe uno con el mismo nombre) y devuelve la ruta.</summary>
        public static string Save(MapData map)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, FileNameFor(map.name) + ".json");
            File.WriteAllText(path, map.ToJson());
            return path;
        }

        /// <summary>Guarda sin pisar: si el nombre ya existe le agrega un número.</summary>
        public static string SaveAsNew(MapData map)
        {
            string baseName = map.name;
            for (int i = 2; File.Exists(Path.Combine(Folder, FileNameFor(map.name) + ".json")); i++)
                map.name = $"{baseName} ({i})";
            return Save(map);
        }

        public static MapData Load(string fileName) =>
            MapData.FromJson(File.ReadAllText(Path.Combine(Folder, fileName + ".json")));
    }
}
