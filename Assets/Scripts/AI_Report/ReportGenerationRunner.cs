using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Runs the AI report pipeline of DriveLicenseSimulatorAIModel (src/report.py: LSTM predict -> OpenAI report) after
// ReportManager exports a full course run. Section practice runs never generate a report.
// Sessions run one at a time in export order. The process writes report.json or report_error.json into the session
// folder and keeps running if this scene unloads, so ReportHistoryController reads the result from those files.
// OPENAI_API_KEY comes from the git-ignored ReportPipelineSettings asset, or else from the OS environment of this PC;
// it is passed only to the child process and is never logged.
[DisallowMultipleComponent]
public class ReportGenerationRunner : MonoBehaviour
{
	private const string ApiKeyVariableName = "OPENAI_API_KEY";
	private const string SectionResultFileName = "section_results.json";
	private const float ProcessPollIntervalSeconds = 0.5f;
	private const int LoggedOutputMaxLength = 2000;

	// Error stages recorded by the runner; src/report.py records "predict" and "report".
	private const string ConfigurationStage = "config";
	private const string LaunchStage = "launch";
	private const string TimeoutStage = "timeout";
	private const string ExitStage = "exit";
	private const string CancelledStage = "cancelled";

	// OpenAI error messages in the process output may echo a partially masked key.
	private static readonly Regex ApiKeyPattern = new Regex(@"sk-[A-Za-z0-9_\-*]+");

	[Header("References")]
	[SerializeField] private ReportManager reportManager;

	[Header("Generation")]
	[Tooltip("Generate an AI report after each full course run. Section practice runs never generate one.")]
	[SerializeField] private bool isReportGenerationEnabled = true;
	[Tooltip("Seconds before a running report process is stopped and recorded as failed.")]
	[SerializeField, Min(1f)] private float processTimeoutSeconds = 600f;

	[Header("Python Pipeline (paths on this PC)")]
	[Tooltip("Full path of python.exe in the AI project's virtual environment (<AI project>\\.venv\\Scripts\\python.exe).")]
	[SerializeField] private string pythonExecutablePath;
	[Tooltip("Full path of the DriveLicenseSimulatorAIModel project root.")]
	[SerializeField] private string aiProjectRootPath;
	[Tooltip("Optional full path of the LSTM checkpoint (.pt). When empty, the report uses official deductions only.")]
	[SerializeField] private string checkpointPath;

	[Header("API Key (PC-local)")]
	[Tooltip("ReportPipelineSettings asset under Assets/LocalSettings/ (git-ignored) holding the OpenAI API key. " +
		"When empty or missing on this PC, the OPENAI_API_KEY environment variable is used.")]
	[SerializeField] private ReportPipelineSettings localSettings;

	private readonly Queue<string> queuedSessionDirectories = new Queue<string>();
	private Coroutine queueCoroutine;

	private void Awake()
	{
		CacheReferences();
	}

	private void OnEnable()
	{
		CacheReferences();
		if (reportManager == null)
		{
			Debug.LogWarning("ReportGenerationRunner: ReportManager reference is missing.", this);
			return;
		}

		reportManager.ReportDataExported += HandleReportDataExported;
	}

	private void OnDisable()
	{
		if (reportManager != null)
		{
			reportManager.ReportDataExported -= HandleReportDataExported;
		}

		CancelQueuedReports();
	}

	private void HandleReportDataExported(string sessionDirectory)
	{
		if (!isReportGenerationEnabled)
		{
			return;
		}

		if (!TryReadIsSectionPractice(sessionDirectory, out bool isSectionPractice) || isSectionPractice)
		{
			return;
		}

		queuedSessionDirectories.Enqueue(sessionDirectory);
		if (queueCoroutine == null)
		{
			queueCoroutine = StartCoroutine(RunQueuedReports());
		}
	}

	private IEnumerator RunQueuedReports()
	{
		while (queuedSessionDirectories.Count > 0)
		{
			string sessionDirectory = queuedSessionDirectories.Dequeue();
			yield return RunReportProcess(sessionDirectory);
		}

		queueCoroutine = null;
	}

	private IEnumerator RunReportProcess(string sessionDirectory)
	{
		if (!TryCreateStartInfo(sessionDirectory, out ProcessStartInfo startInfo, out string configurationError))
		{
			RecordFailure(sessionDirectory, ConfigurationStage, configurationError);
			yield break;
		}

		var processOutput = new ProcessOutputLog();
		Process reportProcess = StartReportProcess(sessionDirectory, startInfo, processOutput);
		if (reportProcess == null)
		{
			yield break;
		}

		Debug.Log($"ReportGenerationRunner: Generating the report for {sessionDirectory}.", this);
		float deadline = Time.realtimeSinceStartup + processTimeoutSeconds;
		while (!reportProcess.HasExited)
		{
			if (Time.realtimeSinceStartup >= deadline)
			{
				StopProcess(reportProcess);
				reportProcess.Dispose();
				RecordFailure(sessionDirectory, TimeoutStage,
					$"report.py did not finish within {processTimeoutSeconds} seconds.");
				yield break;
			}

			yield return new WaitForSecondsRealtime(ProcessPollIntervalSeconds);
		}

		// The parameterless wait also flushes the asynchronous output readers.
		reportProcess.WaitForExit();
		int exitCode = reportProcess.ExitCode;
		reportProcess.Dispose();

		CompleteReportProcess(sessionDirectory, exitCode, processOutput);
	}

	// Marks the session pending and starts the process; on a launch failure records it and returns null.
	private Process StartReportProcess(string sessionDirectory, ProcessStartInfo startInfo,
		ProcessOutputLog processOutput)
	{
		var reportProcess = new Process { StartInfo = startInfo };
		reportProcess.OutputDataReceived += (sender, eventArgs) => processOutput.Append(eventArgs.Data);
		reportProcess.ErrorDataReceived += (sender, eventArgs) => processOutput.Append(eventArgs.Data);

		try
		{
			ReportGenerationFiles.DeleteError(sessionDirectory);
			ReportGenerationFiles.WritePending(sessionDirectory, DateTimeOffset.Now, processTimeoutSeconds);
			reportProcess.Start();
			reportProcess.BeginOutputReadLine();
			reportProcess.BeginErrorReadLine();
		}
		catch (Exception exception) when (exception is Win32Exception || exception is IOException ||
			exception is UnauthorizedAccessException || exception is InvalidOperationException)
		{
			reportProcess.Dispose();
			RecordFailure(sessionDirectory, LaunchStage, $"Failed to start report.py. {exception.Message}");
			return null;
		}

		return reportProcess;
	}

	private void CompleteReportProcess(string sessionDirectory, int exitCode, ProcessOutputLog processOutput)
	{
		string reportPath = Path.Combine(sessionDirectory, ReportGenerationFiles.ReportFileName);
		if (exitCode == 0 && File.Exists(reportPath))
		{
			Debug.Log($"ReportGenerationRunner: Report written to {reportPath}.", this);
			return;
		}

		string outputTail = ApiKeyPattern.Replace(processOutput.ReadTail(LoggedOutputMaxLength), "sk-***");
		string failureMessage = $"report.py exited with code {exitCode}.\n{outputTail}";

		// src/report.py records its own failures; this covers exits before it could (bad arguments, import errors).
		bool hasPythonErrorRecord = File.Exists(Path.Combine(sessionDirectory, ReportGenerationFiles.ErrorFileName));
		if (hasPythonErrorRecord)
		{
			Debug.LogError($"ReportGenerationRunner: Report generation failed for {sessionDirectory}. {failureMessage}",
				this);
			return;
		}

		RecordFailure(sessionDirectory, ExitStage, failureMessage);
	}

	private void CancelQueuedReports()
	{
		if (queueCoroutine != null)
		{
			StopCoroutine(queueCoroutine);
			queueCoroutine = null;
		}

		// A process that already started keeps running and records its own result.
		while (queuedSessionDirectories.Count > 0)
		{
			RecordFailure(queuedSessionDirectories.Dequeue(), CancelledStage,
				"Report generation was cancelled before it started.");
		}
	}

	private bool TryCreateStartInfo(string sessionDirectory, out ProcessStartInfo startInfo, out string error)
	{
		startInfo = null;
		if (!IsExistingFullPath(pythonExecutablePath, File.Exists))
		{
			error = $"Python executable not found: '{pythonExecutablePath}'.";
			return false;
		}

		if (!IsExistingFullPath(aiProjectRootPath, Directory.Exists))
		{
			error = $"AI project root not found: '{aiProjectRootPath}'.";
			return false;
		}

		string reportScriptPath = Path.Combine(aiProjectRootPath, ReportProcessArguments.ReportScriptRelativePath);
		if (!File.Exists(reportScriptPath))
		{
			error = $"report.py not found: '{reportScriptPath}'.";
			return false;
		}

		bool hasCheckpoint = !string.IsNullOrWhiteSpace(checkpointPath);
		if (hasCheckpoint && !IsExistingFullPath(checkpointPath, File.Exists))
		{
			error = $"LSTM checkpoint not found: '{checkpointPath}'.";
			return false;
		}

		// A missing settings asset (git-ignored, so absent on other PCs) compares equal to null.
		bool hasSettingsApiKey = localSettings != null && localSettings.HasOpenAiApiKey;
		bool hasEnvironmentApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyVariableName));
		if (!hasSettingsApiKey && !hasEnvironmentApiKey)
		{
			error = $"No OpenAI API key: set it in ReportPipelineSettings or the {ApiKeyVariableName} environment variable.";
			return false;
		}

		startInfo = new ProcessStartInfo
		{
			FileName = pythonExecutablePath,
			Arguments = ReportProcessArguments.Build(reportScriptPath, sessionDirectory,
				hasCheckpoint ? checkpointPath : null),
			WorkingDirectory = aiProjectRootPath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

		// The Inspector key overrides the inherited environment for the child process only.
		if (hasSettingsApiKey)
		{
			startInfo.EnvironmentVariables[ApiKeyVariableName] = localSettings.OpenAiApiKey;
		}

		error = null;
		return true;
	}

	private bool TryReadIsSectionPractice(string sessionDirectory, out bool isSectionPractice)
	{
		isSectionPractice = false;
		string sectionResultPath = Path.Combine(sessionDirectory, SectionResultFileName);
		try
		{
			var sectionResult = JsonUtility.FromJson<DrivingSectionResultFile>(File.ReadAllText(sectionResultPath));
			isSectionPractice = sectionResult.isSectionPractice;
			return true;
		}
		catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
			exception is ArgumentException)
		{
			Debug.LogError($"ReportGenerationRunner: Failed to read {sectionResultPath}; no report is generated. " +
				exception.Message, this);
			return false;
		}
	}

	private void RecordFailure(string sessionDirectory, string stage, string message)
	{
		Debug.LogError($"ReportGenerationRunner: Report generation failed ({stage}) for {sessionDirectory}. {message}",
			this);
		try
		{
			ReportGenerationFiles.WriteError(sessionDirectory, stage, message);
		}
		catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
		{
			Debug.LogError($"ReportGenerationRunner: Failed to record the failure in {sessionDirectory}. " +
				exception.Message, this);
		}
	}

	private void CacheReferences()
	{
		if (reportManager == null)
		{
			TryGetComponent(out reportManager);
		}
	}

	private static bool IsExistingFullPath(string path, Func<string, bool> exists)
	{
		return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && exists(path);
	}

	private static void StopProcess(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill();
			}
		}
		catch (Exception exception) when (exception is Win32Exception || exception is InvalidOperationException)
		{
			Debug.LogWarning($"ReportGenerationRunner: Failed to stop the report process. {exception.Message}");
		}
	}

	// Collects output lines that arrive on the process reader threads.
	private sealed class ProcessOutputLog
	{
		private readonly StringBuilder outputText = new StringBuilder();
		private readonly object outputLock = new object();

		public void Append(string line)
		{
			if (line == null)
			{
				return;
			}

			lock (outputLock)
			{
				outputText.AppendLine(line);
			}
		}

		public string ReadTail(int maxLength)
		{
			lock (outputLock)
			{
				int startIndex = Math.Max(0, outputText.Length - maxLength);
				return outputText.ToString(startIndex, outputText.Length - startIndex);
			}
		}
	}
}

// Command line of the report process. Arguments are quoted with the Windows (CommandLineToArgvW) rules that
// Python's command-line parser follows, so paths with spaces or a trailing backslash stay intact.
public static class ReportProcessArguments
{
	public const string ReportScriptRelativePath = "src/report.py";

	// Without a checkpoint the report is written from the official deductions only.
	public static string Build(string reportScriptPath, string sessionDirectory, string checkpointPath)
	{
		var arguments = new List<string> { reportScriptPath, sessionDirectory };
		if (string.IsNullOrWhiteSpace(checkpointPath))
		{
			arguments.Add("--without-predictions");
		}
		else
		{
			arguments.Add("--checkpoint");
			arguments.Add(checkpointPath);
		}

		return string.Join(" ", arguments.ConvertAll(Quote));
	}

	public static string Quote(string argument)
	{
		var quotedArgument = new StringBuilder("\"");
		int pendingBackslashCount = 0;
		foreach (char character in argument)
		{
			if (character == '\\')
			{
				pendingBackslashCount++;
				continue;
			}

			// Backslashes are literal unless they precede a quote; there they and the quote are escaped.
			if (character == '"')
			{
				quotedArgument.Append('\\', pendingBackslashCount * 2 + 1);
			}
			else
			{
				quotedArgument.Append('\\', pendingBackslashCount);
			}

			quotedArgument.Append(character);
			pendingBackslashCount = 0;
		}

		// Trailing backslashes precede the closing quote, so they are doubled.
		quotedArgument.Append('\\', pendingBackslashCount * 2);
		quotedArgument.Append('"');
		return quotedArgument.ToString();
	}
}
