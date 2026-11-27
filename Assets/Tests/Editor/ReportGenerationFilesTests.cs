using System;
using System.IO;
using NUnit.Framework;

public class ReportGenerationFilesTests
{
	private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.FromHours(9));

	private string sessionDirectory;

	#region Setup and Teardown

	[SetUp]
	public void SetUp()
	{
		sessionDirectory = Path.Combine(Path.GetTempPath(), "ReportGenerationFilesTests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(sessionDirectory);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(sessionDirectory))
		{
			Directory.Delete(sessionDirectory, true);
		}
	}

	#endregion

	#region Status Resolution

	[Test]
	public void ResolveStatus_NoFiles_IsNotRequested()
	{
		ReportGenerationStatus status = ReportGenerationFiles.ResolveStatus(false, false, false, null, Now);

		Assert.AreEqual(ReportGenerationStatus.NotRequested, status);
	}

	[Test]
	public void ResolveStatus_ReportWithErrorAndPending_IsCompleted()
	{
		ReportGenerationStatus status = ReportGenerationFiles.ResolveStatus(true, true, true, Now.AddMinutes(1), Now);

		Assert.AreEqual(ReportGenerationStatus.Completed, status);
	}

	[Test]
	public void ResolveStatus_ErrorWithPendingBeforeDeadline_IsFailed()
	{
		ReportGenerationStatus status = ReportGenerationFiles.ResolveStatus(false, true, true, Now.AddMinutes(1), Now);

		Assert.AreEqual(ReportGenerationStatus.Failed, status);
	}

	[Test]
	public void ResolveStatus_PendingAtDeadline_IsStillPending()
	{
		ReportGenerationStatus status = ReportGenerationFiles.ResolveStatus(false, false, true, Now, Now);

		Assert.AreEqual(ReportGenerationStatus.Pending, status);
	}

	[Test]
	public void ResolveStatus_PendingPastDeadline_IsFailed()
	{
		ReportGenerationStatus status =
			ReportGenerationFiles.ResolveStatus(false, false, true, Now.AddSeconds(-1), Now);

		Assert.AreEqual(ReportGenerationStatus.Failed, status);
	}

	[Test]
	public void ResolveStatus_PendingWithoutReadableDeadline_IsFailed()
	{
		ReportGenerationStatus status = ReportGenerationFiles.ResolveStatus(false, false, true, null, Now);

		Assert.AreEqual(ReportGenerationStatus.Failed, status);
	}

	#endregion

	#region Session Folder Files

	[Test]
	public void ReadStatus_WrittenPendingWithinTimeout_IsPending()
	{
		ReportGenerationFiles.WritePending(sessionDirectory, Now, 600f);

		ReportGenerationStatus status = ReportGenerationFiles.ReadStatus(sessionDirectory, Now.AddSeconds(600));

		Assert.AreEqual(ReportGenerationStatus.Pending, status);
	}

	[Test]
	public void ReadStatus_WrittenPendingPastTimeoutAndGrace_IsFailed()
	{
		ReportGenerationFiles.WritePending(sessionDirectory, Now, 600f);

		ReportGenerationStatus status = ReportGenerationFiles.ReadStatus(sessionDirectory, Now.AddSeconds(631));

		Assert.AreEqual(ReportGenerationStatus.Failed, status);
	}

	[Test]
	public void ReadStatus_CorruptPendingFile_IsFailed()
	{
		File.WriteAllText(Path.Combine(sessionDirectory, ReportGenerationFiles.PendingFileName), "not json");

		ReportGenerationStatus status = ReportGenerationFiles.ReadStatus(sessionDirectory, Now);

		Assert.AreEqual(ReportGenerationStatus.Failed, status);
	}

	[Test]
	public void ReadStatus_ReportAfterPending_IsCompleted()
	{
		ReportGenerationFiles.WritePending(sessionDirectory, Now, 600f);
		File.WriteAllText(Path.Combine(sessionDirectory, ReportGenerationFiles.ReportFileName), "{}");

		ReportGenerationStatus status = ReportGenerationFiles.ReadStatus(sessionDirectory, Now);

		Assert.AreEqual(ReportGenerationStatus.Completed, status);
	}

	[Test]
	public void WriteError_ThenTryReadError_ReturnsStageAndMessage()
	{
		ReportGenerationFiles.WriteError(sessionDirectory, "timeout", "report.py did not finish.");

		bool hasError = ReportGenerationFiles.TryReadError(sessionDirectory, out ReportErrorFile errorFile);

		Assert.IsTrue(hasError);
		Assert.AreEqual("timeout", errorFile.stage);
		Assert.AreEqual("report.py did not finish.", errorFile.message);
		Assert.AreEqual(ReportGenerationStatus.Failed, ReportGenerationFiles.ReadStatus(sessionDirectory, Now));
	}

	[Test]
	public void TryReadError_PythonErrorFile_ReadsSharedFields()
	{
		// Layout written by src/report.py save_report_error.
		File.WriteAllText(Path.Combine(sessionDirectory, ReportGenerationFiles.ErrorFileName),
			"{\n  \"failedAt\": \"2026-11-26T12:00:00.000000+09:00\",\n  \"stage\": \"report\",\n" +
			"  \"message\": \"ValueError: UI field 1 exceeds the 45-character limit\"\n}");

		bool hasError = ReportGenerationFiles.TryReadError(sessionDirectory, out ReportErrorFile errorFile);

		Assert.IsTrue(hasError);
		Assert.AreEqual("report", errorFile.stage);
		StringAssert.StartsWith("ValueError:", errorFile.message);
	}

	[Test]
	public void DeleteError_RemovesRecordedFailure()
	{
		ReportGenerationFiles.WriteError(sessionDirectory, "exit", "failed");

		ReportGenerationFiles.DeleteError(sessionDirectory);

		Assert.IsFalse(File.Exists(Path.Combine(sessionDirectory, ReportGenerationFiles.ErrorFileName)));
	}

	#endregion
}
