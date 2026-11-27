using NUnit.Framework;

public class ReportProcessArgumentsTests
{
	[TestCase(@"C:\AI Model\src\report.py", "\"C:\\AI Model\\src\\report.py\"")]
	[TestCase(@"C:\Session\", "\"C:\\Session\\\\\"")]
	[TestCase("a\"b", "\"a\\\"b\"")]
	[TestCase(@"a\""b", "\"a\\\\\\\"b\"")]
	[TestCase("", "\"\"")]
	public void Quote_WindowsArgument_EscapesQuotesAndTrailingBackslashes(string argument, string expected)
	{
		Assert.AreEqual(expected, ReportProcessArguments.Quote(argument));
	}

	[Test]
	public void Build_WithCheckpoint_PassesCheckpointArgument()
	{
		string arguments = ReportProcessArguments.Build(@"C:\AI\src\report.py", @"C:\Data\AIReport\20261126", @"C:\AI\lstm.pt");

		Assert.AreEqual(
			"\"C:\\AI\\src\\report.py\" \"C:\\Data\\AIReport\\20261126\" \"--checkpoint\" \"C:\\AI\\lstm.pt\"",
			arguments);
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public void Build_WithoutCheckpoint_UsesOfficialDeductionsOnly(string checkpointPath)
	{
		string arguments = ReportProcessArguments.Build(@"C:\AI\src\report.py", @"C:\Data\Session", checkpointPath);

		Assert.AreEqual("\"C:\\AI\\src\\report.py\" \"C:\\Data\\Session\" \"--without-predictions\"", arguments);
	}
}
