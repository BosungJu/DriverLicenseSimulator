using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Generation state of a session's AI report, read from files in the session folder.
public enum ReportGenerationStatus
{
	// No report was requested: a section practice run, generation turned off, or an older session.
	NotRequested,
	Pending,
	Completed,
	Failed
}

// report_pending.json, written by ReportGenerationRunner just before it starts src/report.py.
[Serializable]
public class ReportPendingFile
{
	public string requestedAt;
	public float timeoutSeconds;
}

// report_error.json, written by src/report.py (save_report_error) or by ReportGenerationRunner when the process
// could not start, timed out or exited without a result. Keep the fields in sync with the Python side.
[Serializable]
public class ReportErrorFile
{
	public string failedAt;
	public string stage;
	public string message;
}

// Session folder files that track AI report generation: the Python pipeline writes report.json on success and
// report_error.json on failure, and ReportGenerationRunner writes report_pending.json when it starts the pipeline.
// Because the state lives in files, ReportHistoryController can read it after the driving scene is unloaded.
public static class ReportGenerationFiles
{
	public const string ReportFileName = "report.json";
	public const string ErrorFileName = "report_error.json";
	public const string PendingFileName = "report_pending.json";

	// Extra time past the runner's timeout before a pending request counts as failed, so the runner can stop the
	// process and record the failure itself first.
	private const float PendingGraceSeconds = 30f;

	// Encoding.UTF8 writes a BOM, which Python's json module rejects with the plain utf-8 codec.
	private static readonly Encoding FileEncoding = new UTF8Encoding(false);

	// report.json wins over a failure record, so a report that lands after a timeout is still shown.
	// A pending request without a readable deadline, or past it, is failed: nothing will write its result.
	public static ReportGenerationStatus ResolveStatus(bool hasReport, bool hasError, bool hasPending,
		DateTimeOffset? pendingDeadline, DateTimeOffset now)
	{
		if (hasReport)
		{
			return ReportGenerationStatus.Completed;
		}

		if (hasError)
		{
			return ReportGenerationStatus.Failed;
		}

		if (!hasPending)
		{
			return ReportGenerationStatus.NotRequested;
		}

		bool isPastDeadline = !pendingDeadline.HasValue || now > pendingDeadline.Value;
		return isPastDeadline ? ReportGenerationStatus.Failed : ReportGenerationStatus.Pending;
	}

	public static ReportGenerationStatus ReadStatus(string sessionDirectory, DateTimeOffset now)
	{
		bool hasReport = File.Exists(Path.Combine(sessionDirectory, ReportFileName));
		bool hasError = File.Exists(Path.Combine(sessionDirectory, ErrorFileName));
		string pendingPath = Path.Combine(sessionDirectory, PendingFileName);
		bool hasPending = File.Exists(pendingPath);

		DateTimeOffset? pendingDeadline = null;
		if (hasPending && !hasReport && !hasError)
		{
			pendingDeadline = ReadPendingDeadline(pendingPath);
		}

		return ResolveStatus(hasReport, hasError, hasPending, pendingDeadline, now);
	}

	public static void WritePending(string sessionDirectory, DateTimeOffset requestedAt, float timeoutSeconds)
	{
		var pendingFile = new ReportPendingFile
		{
			requestedAt = requestedAt.ToString("o", CultureInfo.InvariantCulture),
			timeoutSeconds = timeoutSeconds
		};

		File.WriteAllText(Path.Combine(sessionDirectory, PendingFileName), JsonUtility.ToJson(pendingFile, true),
			FileEncoding);
	}

	public static void WriteError(string sessionDirectory, string stage, string message)
	{
		var errorFile = new ReportErrorFile
		{
			failedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
			stage = stage,
			message = message
		};

		File.WriteAllText(Path.Combine(sessionDirectory, ErrorFileName), JsonUtility.ToJson(errorFile, true),
			FileEncoding);
	}

	public static void DeleteError(string sessionDirectory)
	{
		File.Delete(Path.Combine(sessionDirectory, ErrorFileName));
	}

	public static bool TryReadError(string sessionDirectory, out ReportErrorFile errorFile)
	{
		errorFile = null;
		string errorPath = Path.Combine(sessionDirectory, ErrorFileName);
		if (!File.Exists(errorPath))
		{
			return false;
		}

		try
		{
			errorFile = JsonUtility.FromJson<ReportErrorFile>(File.ReadAllText(errorPath));
		}
		catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
			exception is ArgumentException)
		{
			Debug.LogWarning($"ReportGenerationFiles: Failed to read {errorPath}. {exception.Message}");
			return false;
		}

		return errorFile != null;
	}

	// Returns null when the file cannot be read, which ResolveStatus treats as failed.
	private static DateTimeOffset? ReadPendingDeadline(string pendingPath)
	{
		ReportPendingFile pendingFile;
		try
		{
			pendingFile = JsonUtility.FromJson<ReportPendingFile>(File.ReadAllText(pendingPath));
		}
		catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
			exception is ArgumentException)
		{
			Debug.LogWarning($"ReportGenerationFiles: Failed to read {pendingPath}. {exception.Message}");
			return null;
		}

		if (pendingFile == null || pendingFile.timeoutSeconds <= 0f ||
			!DateTimeOffset.TryParse(pendingFile.requestedAt, CultureInfo.InvariantCulture,
				DateTimeStyles.None, out DateTimeOffset requestedAt))
		{
			return null;
		}

		return requestedAt.AddSeconds(pendingFile.timeoutSeconds + PendingGraceSeconds);
	}
}
