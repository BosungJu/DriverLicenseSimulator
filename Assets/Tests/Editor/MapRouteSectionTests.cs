using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class MapRouteSectionTests
{
    private GameObject sectionObject;
    private GameObject vehicle;
    private MapRouteSection section;

    #region Setup and Teardown

    [SetUp]
    public void SetUp()
    {
        sectionObject = new GameObject("Section test");
        vehicle = new GameObject("Vehicle test");
        section = sectionObject.AddComponent<MapRouteSection>();
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(sectionObject);
        UnityEngine.Object.DestroyImmediate(vehicle);
    }

    private void Initialize(string commands)
    {
        List<MapData> points = CsvParaser.GetMapDataFromText("name,layer,pos_x,pos_y,pos_z\nPoint,POINT,12000,34000,0");
        Assert.That(MapRouteInstruction.TryParse(commands, points, out var instructions, out string error), Is.True, error);
        section.Initialize(0, points[0], instructions[0]);
    }

    #endregion

    #region Mapping and Lifecycle

	[TestCase(0)]
	[TestCase(2)]
	public void Initialize_WithoutInstruction_UsesZeroBasedRouteIndex(int routeIndex)
	{
		List<MapData> points = CsvParaser.GetMapDataFromText("name,layer,pos_z\nPoint,POINT,0");

		section.Initialize(routeIndex, points[0], null);

		Assert.That(section.RouteIndex, Is.EqualTo(routeIndex));
		Assert.That(section.PointNumber, Is.EqualTo(routeIndex));
		Assert.That(section.Commands, Is.Empty);
		Assert.That(section.Actions, Is.Empty);
		Assert.That(section.InstructionText, Is.Empty);
	}

    [TestCase("start", typeof(StartSectionAction))]
    [TestCase("hill", typeof(HillSectionAction))]
    [TestCase("left", typeof(LeftTurnSectionAction))]
    [TestCase("right", typeof(RightTurnSectionAction))]
    [TestCase("signal", typeof(SignalSectionAction))]
    [TestCase("T_TEST", typeof(ParkingSectionAction))]
    [TestCase("fast", typeof(AccelerationSectionAction))]
    [TestCase("on_emergency", typeof(EmergencySectionAction))]
    [TestCase("end", typeof(FinishSectionAction))]
    [TestCase("custom", typeof(CustomSectionAction))]
    public void Initialize_Command_CreatesMatchingAction(string command, Type actionType)
    {
        Initialize(command);

        Assert.That(section.Actions[0], Is.TypeOf(actionType));
        Assert.That(section.Commands[0], Is.EqualTo(command));
        Assert.That(section.PointNumber, Is.Zero);
        Assert.That(section.CadPosition, Is.EqualTo(new Vector2(12000, 34000)));
    }

    [TestCase("hill_0", typeof(HillSectionAction), "hill", 0)]
    [TestCase("hill_2", typeof(HillSectionAction), "hill", 2)]
    [TestCase("T_TEST_3", typeof(ParkingSectionAction), "T_TEST", 3)]
    public void Initialize_StepCommand_CreatesBaseActionWithStep(string command, Type actionType,
        string expectedName, int expectedStep)
    {
        Initialize(command);

        DrivingSectionAction action = section.Actions[0];
        Assert.That(action, Is.TypeOf(actionType));
        Assert.That(action.Command, Is.EqualTo(command));
        Assert.That(action.CommandName, Is.EqualTo(expectedName));
        Assert.That(action.StepIndex, Is.EqualTo(expectedStep));
        Assert.That(action.HasStep, Is.True);
    }

    [TestCase("hill", "hill")]
    [TestCase("T_TEST", "T_TEST")]
    [TestCase("on_emergency", "on_emergency")]
    [TestCase("hill_", "hill_")]
    [TestCase("_0", "_0")]
    [TestCase("hill_a1", "hill_a1")]
    public void SplitStep_NoNumericSuffix_KeepsWholeNameWithoutStep(string command, string expectedName)
    {
        DrivingSectionAction.SplitStep(command, out string commandName, out int stepIndex);

        Assert.That(commandName, Is.EqualTo(expectedName));
        Assert.That(stepIndex, Is.EqualTo(DrivingSectionAction.NoStep));
    }

    [Test]
    public void SplitStep_NumericSuffix_SeparatesNameAndStep()
    {
        DrivingSectionAction.SplitStep(" T_TEST_12 ", out string commandName, out int stepIndex);

        Assert.That(commandName, Is.EqualTo("T_TEST"));
        Assert.That(stepIndex, Is.EqualTo(12));
    }

    [Test]
    public void Enter_CompoundSection_ActivatesAllActionsBeforeNotification()
    {
        Initialize("signal | right");
        int enteredCount = 0;
        section.Actions[0].Entered += action =>
        {
            enteredCount++;
            Assert.That(section.Actions[1].State, Is.EqualTo(DrivingSectionState.Active));
            Assert.That(action.Vehicle, Is.SameAs(vehicle));
        };

        section.Enter(vehicle);
        section.Enter(vehicle);
        section.Tick(0.25f);
        section.Tick(0.5f);
        section.Complete();
        section.Tick(1f);

        Assert.That(enteredCount, Is.EqualTo(1));
        Assert.That(section.State, Is.EqualTo(DrivingSectionState.Completed));
        foreach (DrivingSectionAction action in section.Actions)
        {
            Assert.That(action.State, Is.EqualTo(DrivingSectionState.Completed));
            Assert.That(action.ElapsedSeconds, Is.EqualTo(0.75f));
        }
    }

    [Test]
    public void ResetSection_ActiveSection_CancelsAndAllowsReuse()
    {
        Initialize("hill");
        int cancelledCount = 0;
        int completedCount = 0;
        section.Cancelled += _ => cancelledCount++;
        section.Exited += _ => completedCount++;
        section.Enter(vehicle);
        section.Tick(2f);

        section.ResetSection();

        Assert.That(cancelledCount, Is.EqualTo(1));
        Assert.That(completedCount, Is.Zero);
        Assert.That(section.Actions[0].Vehicle, Is.Null);
        Assert.That(section.Actions[0].ElapsedSeconds, Is.Zero);
        section.Enter(vehicle);
        section.Complete();
        section.Complete();
        Assert.That(completedCount, Is.EqualTo(1));
    }

    #endregion

    #region Cancellation and Invalid Entry

    [Test]
    public void Enter_ResetInsideActionEvent_StopsRemainingNotifications()
    {
        Initialize("left | on_emergency");
        int secondEnteredCount = 0;
        section.Actions[0].Entered += _ => section.ResetSection();
        section.Actions[1].Entered += _ => secondEnteredCount++;

        section.Enter(vehicle);

        Assert.That(secondEnteredCount, Is.Zero);
        Assert.That(section.State, Is.EqualTo(DrivingSectionState.Ready));
        Assert.That(section.Actions[1].State, Is.EqualTo(DrivingSectionState.Ready));
    }

    [Test]
    public void Enter_NullVehicle_DoesNotStartSection()
    {
        Initialize("start");

        section.Enter(null);
        section.Tick(1f);

        Assert.That(section.State, Is.EqualTo(DrivingSectionState.Ready));
        Assert.That(section.Actions[0].ElapsedSeconds, Is.Zero);
    }

    #endregion
}
