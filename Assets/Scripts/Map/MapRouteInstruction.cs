using System;
using System.Collections.Generic;

public sealed class MapRouteInstruction
{
    public int PointNumber { get; }
    public string Text { get; }
    public IReadOnlyList<string> Commands { get; }

    private MapRouteInstruction(int pointNumber, string text, string[] commands)
    {
        PointNumber = pointNumber;
        Text = text;
        Commands = Array.AsReadOnly(commands);
    }

    public static bool TryParse(string instructionText, IReadOnlyList<MapData> sortedRoutePoints,
        out IReadOnlyList<MapRouteInstruction> instructions, out string error)
    {
        instructions = Array.Empty<MapRouteInstruction>();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(instructionText))
        {
            error = "DATA instruction text is empty.";
            return false;
        }

        string[] pointInstructions = instructionText.Split(',');
        if (pointInstructions.Length != sortedRoutePoints.Count)
        {
            error = $"DATA has {pointInstructions.Length} instructions, but POINT has {sortedRoutePoints.Count} points.";
            return false;
        }

        var parsedInstructions = new List<MapRouteInstruction>(pointInstructions.Length);
        for (int i = 0; i < pointInstructions.Length; i++)
        {
            // POINT Z is the zero-based pass order written in CAD.
            int pointNumber = i;
            if (sortedRoutePoints[i].PosZ != pointNumber)
            {
                error = $"POINT Z must run from 0 to {sortedRoutePoints.Count - 1}. Expected {pointNumber}, found {sortedRoutePoints[i].PosZ}.";
                return false;
            }

            string text = pointInstructions[i].Trim();
            string[] commands = text.Split('|');
            for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
            {
                commands[commandIndex] = commands[commandIndex].Trim();
                if (commands[commandIndex].Length == 0)
                {
                    error = $"DATA contains an empty instruction at Point {pointNumber}.";
                    return false;
                }
            }

            parsedInstructions.Add(new MapRouteInstruction(pointNumber, text, commands));
        }

        instructions = parsedInstructions.AsReadOnly();
        return true;
    }
}
