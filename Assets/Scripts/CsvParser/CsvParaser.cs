using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class CsvParaser
{
    public const string mapData = "MapData/옥천학원수정";

    public static List<MapData> GetMapData(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            filename = mapData;
        }
        else
        {
            if (filename.EndsWith(".csv"))
            {
                filename = filename.Substring(0, filename.Length - 4);
            }

            if (!filename.Contains("/") && !filename.StartsWith("MapData"))
            {
                filename = $"MapData/{filename}";
            }
        }

        var textAsset = Resources.Load<TextAsset>(filename);
        if (textAsset == null)
        {
            Debug.LogError($"CsvParaser: failed to load CSV file at Resources/{filename}.csv");
            return new List<MapData>();
        }

        return ParseMapDataCsv(textAsset.text);
    }

    private static List<MapData> ParseMapDataCsv(string csvText)
    {
        var lines = csvText.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None);
        var list = new List<MapData>();
        if (lines.Length <= 1)
        {
            return list;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            var columns = line.Split(',');
            if (columns.Length < 47)
            {
                // CSV row may be missing trailing empty columns; pad if necessary.
                System.Array.Resize(ref columns, 47);
            }

            list.Add(CreateMapData(columns));
        }

        return list;
    }

    private static MapData CreateMapData(string[] columns)
    {
        return new MapData(
            ToInt(columns, 0),
            GetString(columns, 1),
            GetString(columns, 2),
            ToFloat(columns, 3),
            ToFloat(columns, 4),
            ToFloat(columns, 5),
            ToFloat(columns, 6),
            ToInt(columns, 7),
            ToFloat(columns, 8),
            ToFloat(columns, 9),
            ToFloat(columns, 10),
            GetString(columns, 11),
            ToFloat(columns, 12),
            ToFloat(columns, 13),
            ToFloat(columns, 14),
            ToFloat(columns, 15),
            ToFloat(columns, 16),
            ToFloat(columns, 17),
            ToFloat(columns, 18),
            ToFloat(columns, 19),
            ToFloat(columns, 20),
            ToFloat(columns, 21),
            ToFloat(columns, 22),
            ToFloat(columns, 23),
            ToFloat(columns, 24),
            ToFloat(columns, 25),
            ToFloat(columns, 26),
            ToFloat(columns, 27),
            ToFloat(columns, 28),
            ToFloat(columns, 29),
            ToFloat(columns, 30),
            ToFloat(columns, 31),
            ToFloat(columns, 32),
            ToFloat(columns, 33),
            ToFloat(columns, 34),
            ToFloat(columns, 35),
            ToFloat(columns, 36),
            ToFloat(columns, 37),
            ToInt(columns, 38),
            ToFloat(columns, 39),
            ToFloat(columns, 40),
            ToFloat(columns, 41),
            GetString(columns, 42),
            ToInt(columns, 43),
            GetString(columns, 44),
            ToFloat(columns, 45),
            ToFloat(columns, 46)
        );
    }

    private static string GetString(string[] columns, int index)
    {
        return index < columns.Length && !string.IsNullOrEmpty(columns[index]) ? columns[index].Trim() : string.Empty;
    }

    private static int ToInt(string[] columns, int index)
    {
        if (index < columns.Length && int.TryParse(columns[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return 0;
    }

    private static float ToFloat(string[] columns, int index)
    {
        if (index < columns.Length && float.TryParse(columns[index].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return 0f;
    }
}
