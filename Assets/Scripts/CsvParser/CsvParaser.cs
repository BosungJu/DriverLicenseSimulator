using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class CsvParaser
{
    public const string mapData = "MapData/옥천학원수정_origin";
    private const int MapDataColumnCount = 47;

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

    public static List<MapData> GetMapDataFromCsvFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Debug.LogError($"CsvParaser: CSV file does not exist. Path: {filePath}");
            return new List<MapData>();
        }

        return ParseMapDataCsv(File.ReadAllText(filePath));
    }

    public static List<MapData> GetMapDataFromText(string csvText)
    {
        return ParseMapDataCsv(csvText);
    }

    private static List<MapData> ParseMapDataCsv(string csvText)
    {
        var records = SplitCsvRecords(csvText);
        var list = new List<MapData>(Mathf.Max(0, records.Count - 1));
        if (records.Count <= 1)
        {
            return list;
        }

        var headerLookup = CreateHeaderLookup(ParseCsvLine(records[0]));
        for (int i = 1; i < records.Count; i++)
        {
            var line = records[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            var columns = NormalizeColumns(ParseCsvLine(line).ToArray(), headerLookup);

            list.Add(CreateMapData(columns));
        }

        return list;
    }

    private static List<string> SplitCsvRecords(string csvText)
    {
        var records = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < csvText.Length; i++)
        {
            char character = csvText[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < csvText.Length && csvText[i + 1] == '"')
                {
                    current.Append(character);
                    current.Append(csvText[i + 1]);
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                    current.Append(character);
                }

                continue;
            }

            if ((character == '\n' || character == '\r') && !inQuotes)
            {
                if (character == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n')
                {
                    i++;
                }

                records.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            records.Add(current.ToString());
        }

        return records;
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
            ToFloat(columns, 38),
            ToFloat(columns, 39),
            ToFloat(columns, 40),
            ToFloat(columns, 41),
            GetString(columns, 42),
            ToFloat(columns, 43),
            GetString(columns, 44),
            ToFloat(columns, 45),
            ToFloat(columns, 46)
        );
    }

    private static List<string> ParseCsvLine(string line)
    {
        var columns = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char character = line[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (character == ',' && !inQuotes)
            {
                columns.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        columns.Add(current.ToString());
        return columns;
    }

    private static Dictionary<string, int> CreateHeaderLookup(List<string> headers)
    {
        var headerLookup = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            var header = headers[i].Trim();
            if (!string.IsNullOrEmpty(header) && !headerLookup.ContainsKey(header))
            {
                headerLookup.Add(header, i);
            }
        }

        return headerLookup;
    }

    private static string[] NormalizeColumns(string[] sourceColumns, Dictionary<string, int> headerLookup)
    {
        var columns = new string[MapDataColumnCount];
        columns[0] = GetColumn(sourceColumns, headerLookup, "index", "계수");
        columns[1] = NormalizeEntityName(GetColumn(sourceColumns, headerLookup, "name", "이름"));
        columns[2] = GetColumn(sourceColumns, headerLookup, "layer", "도면층");
        columns[3] = GetColumn(sourceColumns, headerLookup, "pos_x", "위치 X");
        columns[4] = GetColumn(sourceColumns, headerLookup, "pos_y", "위치 Y");
        columns[5] = GetColumn(sourceColumns, headerLookup, "pos_z", "위치 Z");
        columns[6] = GetColumn(sourceColumns, headerLookup, "rotation", "회전");
        columns[7] = GetColumn(sourceColumns, headerLookup, "value", "값");
        columns[8] = GetColumn(sourceColumns, headerLookup, "tilt", "기울기");
        columns[9] = GetColumn(sourceColumns, headerLookup, "height", "높이");
        columns[10] = GetColumn(sourceColumns, headerLookup, "thickness", "두께");
        columns[11] = GetColumn(sourceColumns, headerLookup, "style", "스타일");
        columns[12] = GetColumn(sourceColumns, headerLookup, "width_ratio", "폭 비율");
        columns[13] = GetColumn(sourceColumns, headerLookup, "degree", "각도");
        columns[14] = GetColumn(sourceColumns, headerLookup, "length", "길이");
        columns[15] = GetColumn(sourceColumns, headerLookup, "end_x", "끝 X");
        columns[16] = GetColumn(sourceColumns, headerLookup, "end_y", "끝 Y");
        columns[17] = GetColumn(sourceColumns, headerLookup, "end_z", "끝 Z");
        columns[18] = GetColumn(sourceColumns, headerLookup, "delta_x", "델타 X");
        columns[19] = GetColumn(sourceColumns, headerLookup, "delta_y", "델타 Y");
        columns[20] = GetColumn(sourceColumns, headerLookup, "delta_z", "델타 Z");
        columns[21] = GetColumn(sourceColumns, headerLookup, "start_x", "시작 X");
        columns[22] = GetColumn(sourceColumns, headerLookup, "start_y", "시작 Y");
        columns[23] = GetColumn(sourceColumns, headerLookup, "start_z", "시작 Z");
        columns[24] = GetColumn(sourceColumns, headerLookup, "pos_x1", "위치 X1");
        columns[25] = GetColumn(sourceColumns, headerLookup, "pos_y1", "위치 Y1");
        columns[26] = GetColumn(sourceColumns, headerLookup, "pos_z1", "위치 Z1");
        columns[27] = GetColumn(sourceColumns, headerLookup, "width", "폭");
        columns[28] = GetColumn(sourceColumns, headerLookup, "minor_radius", "단축 반지름");
        columns[29] = GetColumn(sourceColumns, headerLookup, "minor_vector_x", "단축 벡터 X");
        columns[30] = GetColumn(sourceColumns, headerLookup, "minor_vector_y", "단축 벡터 Y");
        columns[31] = GetColumn(sourceColumns, headerLookup, "minor_vector_z", "단축 벡터 Z");
        columns[32] = GetColumn(sourceColumns, headerLookup, "area", "면적");
        columns[33] = GetColumn(sourceColumns, headerLookup, "radius_ratio", "반지름 비율");
        columns[34] = GetColumn(sourceColumns, headerLookup, "start_degree", "시작 각도");
        columns[35] = GetColumn(sourceColumns, headerLookup, "major_radius", "장축 반지름");
        columns[36] = GetColumn(sourceColumns, headerLookup, "major_vector_x", "장축 벡터 X");
        columns[37] = GetColumn(sourceColumns, headerLookup, "major_vector_y", "장축 벡터 Y");
        columns[38] = GetColumn(sourceColumns, headerLookup, "major_vector_z", "장축 벡터 Z");
        columns[39] = GetColumn(sourceColumns, headerLookup, "centor_point_x", "중심점 X");
        columns[40] = GetColumn(sourceColumns, headerLookup, "centor_point_y", "중심점 Y");
        columns[41] = GetColumn(sourceColumns, headerLookup, "centor_point_z", "중심점 Z");
        columns[42] = NormalizeBool(GetColumn(sourceColumns, headerLookup, "close", "닫기"));
        columns[43] = GetColumn(sourceColumns, headerLookup, "global_width", "전역 폭");
        columns[44] = GetColumn(sourceColumns, headerLookup, "associative", "연관");
        columns[45] = GetColumn(sourceColumns, headerLookup, "radius", "반지름");
        columns[46] = GetColumn(sourceColumns, headerLookup, "total_angle", "전체 각도");
        return columns;
    }

    private static string GetColumn(string[] sourceColumns, Dictionary<string, int> headerLookup, params string[] headerNames)
    {
        foreach (var headerName in headerNames)
        {
            if (headerLookup.TryGetValue(headerName, out int index) && index < sourceColumns.Length)
            {
                return sourceColumns[index];
            }
        }

        return string.Empty;
    }

    private static string NormalizeEntityName(string name)
    {
        switch (name.Trim())
        {
            case "선":
                return "Line";
            case "폴리선":
                return "PolyLine";
            case "호":
                return "Arc";
            case "타원":
                return "Ellipse";
            case "원":
                return "Circle";
            case "점":
                return "Point";
            default:
                return name.Trim();
        }
    }

    private static string NormalizeBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Equals("true", System.StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE";
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
