// Records deductions against the route point that is current when a rule fires,
// so section judges do not need to know which point they belong to.
public sealed class DrivingDeductionRecorder
{
	private readonly DrivingScoreSheet scoreSheet;

	public int CurrentPointNumber { get; private set; } = DrivingTestLevel.NoCurrentPoint;

	public DrivingDeductionRecorder(DrivingScoreSheet scoreSheet)
	{
		this.scoreSheet = scoreSheet;
	}

	public void AssignPointNumber(int pointNumber)
	{
		CurrentPointNumber = pointNumber;
	}

	public void Deduct(DrivingDeductionReason reason, int points, string command)
	{
		scoreSheet.ApplyDeduction(new DrivingDeduction(CurrentPointNumber, command, reason, points));
	}

	public void Disqualify(DrivingDeductionReason reason, string command)
	{
		scoreSheet.ApplyDeduction(new DrivingDeduction(CurrentPointNumber, command, reason, 0, true));
	}
}
