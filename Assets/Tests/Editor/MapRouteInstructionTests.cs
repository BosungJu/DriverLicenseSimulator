using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public class MapRouteInstructionTests
{
    #region Normal Cases

    [Test]
    public void TryParse_CompoundInstruction_StartsAtTheSamePoint()
    {
        List<MapData> points = CreatePoints("0", "1", "2");

        bool succeeded = MapRouteInstruction.TryParse("start, left | on_emergency, end", points,
            out IReadOnlyList<MapRouteInstruction> instructions, out string error);

        Assert.That(succeeded, Is.True, error);
        Assert.That(instructions.Count, Is.EqualTo(3));
        Assert.That(instructions[0].PointNumber, Is.Zero);
        Assert.That(instructions[1].PointNumber, Is.EqualTo(1));
        Assert.That(instructions[1].Commands, Is.EqualTo(new[] { "left", "on_emergency" }));
        Assert.That(instructions[2].Commands, Is.EqualTo(new[] { "end" }));
    }

    [TestCase("\"start, left | on_emergency, end\"")]
    [TestCase("start, left | on_emergency, end")]
    public void GetMapDataFromText_DataCommas_PreservesTextAndFollowingColumns(string textColumn)
    {
        string csv = "name,layer,value,tilt,height,style\n"
            + "TEXT,DATA," + textColumn + ",0,12.5,Standard";

        MapData instructionData = CsvParaser.GetMapDataFromText(csv)[0];

        Assert.That(instructionData.Text, Is.EqualTo("start, left | on_emergency, end"));
        Assert.That(instructionData.Height, Is.EqualTo(12.5f));
        Assert.That(instructionData.Style, Is.EqualTo("Standard"));
    }

    [Test]
    public void GetMapDataFromText_NumericText_PreservesExistingValue()
    {
        MapData data = CsvParaser.GetMapDataFromText("name,layer,value\nTEXT,DIM,123")[0];

        Assert.That(data.Value, Is.EqualTo(123));
        Assert.That(data.Text, Is.EqualTo("123"));
    }

    #endregion

    #region Current Map Regression

	[Test]
	public void TryParse_BongdamCsv_MapsSixteenPointsAndCompletesWithoutDeductions()
	{
		string csvPath = Path.Combine(Application.dataPath, "Resources", "MapData", "Data_Road_Bongdam_Points.csv");
		List<MapData> mapData = CsvParaser.GetMapDataFromCsvFile(csvPath);
		List<MapData> points = mapData.FindAll(point => point.Name == "Point"
			&& string.Equals(point.Layer, "Point", System.StringComparison.OrdinalIgnoreCase));
		points.Sort((left, right) => left.PosZ.CompareTo(right.PosZ));
		List<MapData> dataTexts = mapData.FindAll(data => data.Name == "Text"
			&& string.Equals(data.Layer, "Data", System.StringComparison.OrdinalIgnoreCase));
		Assert.That(points.Count, Is.EqualTo(16));
		Assert.That(dataTexts.Count, Is.EqualTo(1));
		Assert.That(MapRouteInstruction.TryParse(dataTexts[0].Text, points,
			out IReadOnlyList<MapRouteInstruction> instructions, out string error), Is.True, error);
		string[] expected = { "start", "hill_0", "hill_1", "hill_2 | left", "left", "signal", "right",
			"T_Test_0", "T_Test_1", "T_Test_2", "T_Test_3 | left", "signal", "left", "fast_0", "fast_1", "end" };
		var evaluator = new DrivingTestScoreEvaluator(new DrivingScoringSettings());
		var drivingControls = new DrivingVehicleState(true, true, false, true, true, false);
		var parkingControls = new DrivingVehicleState(true, true, false, false, false, true);

		for (int i = 0; i < instructions.Count; i++)
		{
			Assert.That(instructions[i].PointNumber, Is.EqualTo(i));
			Assert.That(instructions[i].Text, Is.EqualTo(expected[i]));
			evaluator.BeginSection(i, instructions[i].Commands);
			if (i == 2)
			{
				evaluator.Tick(0.1f, 0f);
				evaluator.Tick(3f, 0f);
			}
			else if (i == 9)
			{
				evaluator.Tick(1f, 0f, vehicleState: parkingControls);
			}
			else
			{
				evaluator.Tick(1f, i == 13 ? 20f : 10f, vehicleState: drivingControls);
			}
		}

		evaluator.CompleteCourse();

		Assert.That(evaluator.IsCourseRunning, Is.False);
		Assert.That(evaluator.ScoreSheet.Deductions, Is.Empty);
		Assert.That(evaluator.ScoreSheet.Score, Is.EqualTo(100));
		Assert.That(evaluator.ScoreSheet.IsPassed, Is.True);
	}

	[Test]
	public void TryParse_CurrentMapCsv_MapsAllZeroBasedPointsToCommands()
	{
		string csvPath = Path.Combine(Application.dataPath, "Resources", "MapData", "옥천학원수정.csv");
		List<MapData> mapData = CsvParaser.GetMapDataFromCsvFile(csvPath);
		List<MapData> points = mapData.FindAll(point => point.Name == "Point" && point.Layer == "POINT");
		points.Sort((left, right) => left.PosZ.CompareTo(right.PosZ));
		List<MapData> instructionData = mapData.FindAll(data => data.Name == "Text" && data.Layer == "DATA");
		Assert.That(points.Count, Is.EqualTo(23));
		Assert.That(instructionData.Count, Is.EqualTo(1));

		bool succeeded = MapRouteInstruction.TryParse(instructionData[0].Text, points,
			out IReadOnlyList<MapRouteInstruction> instructions, out string error);

		Assert.That(succeeded, Is.True, error);
		Assert.That(instructions.Count, Is.EqualTo(23));
		for (int i = 0; i < instructions.Count; i++)
		{
			Assert.That(instructions[i].PointNumber, Is.EqualTo(i));
		}
		Assert.That(instructions[0].Commands, Is.EqualTo(new[] { "start" }));
		Assert.That(instructions[5].Commands, Is.EqualTo(new[] { "left", "on_emergency" }));
		Assert.That(instructions[22].Commands, Is.EqualTo(new[] { "end" }));
	}

    #endregion

    #region Invalid Route Data

    [TestCase("start,end", "0", "0")]
    [TestCase("start,end", "-1", "0")]
    [TestCase("start,end", "1", "2")]
    [TestCase("start,end", "0", "2")]
    [TestCase("start,end", "0", "1.5")]
    [TestCase("start", "0", "1")]
    [TestCase("start,,end", "0", "1")]
    [TestCase("start,", "0", "1")]
    [TestCase("start,left |", "0", "1")]
    [TestCase("", "0", "1")]
    public void TryParse_InvalidData_ReturnsErrorWithoutPartialInstructions(string text, string firstZ, string secondZ)
    {
        bool succeeded = MapRouteInstruction.TryParse(text, CreatePoints(firstZ, secondZ),
            out IReadOnlyList<MapRouteInstruction> instructions, out string error);

        Assert.That(succeeded, Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(instructions, Is.Empty);
    }

	[TestCase("start,,end")]
	[TestCase("start, | left,end")]
	public void TryParse_EmptyCommandWithMatchingPointCount_ReturnsErrorWithoutPartialInstructions(string text)
	{
		bool succeeded = MapRouteInstruction.TryParse(text, CreatePoints("0", "1", "2"),
			out IReadOnlyList<MapRouteInstruction> instructions, out string error);

		Assert.That(succeeded, Is.False);
		Assert.That(error, Does.Contain("Point 1"));
		Assert.That(instructions, Is.Empty);
	}

    #endregion

    private static List<MapData> CreatePoints(params string[] pointNumbers)
    {
        string csv = "name,layer,pos_z\n";
        foreach (string pointNumber in pointNumbers)
        {
            csv += $"Point,POINT,{pointNumber}\n";
        }

        return CsvParaser.GetMapDataFromText(csv);
    }
}
