using System;
using System.IO;
using System.Text;
using UnityEngine;

// Collects AI training data: when ReportManager exports a finished course, the recorded samples are converted
// into unlabeled section records (training_sections.jsonl) in the same session folder.
// Merge the session files and add split/label_names/labels offline to build the training set.
[DisallowMultipleComponent]
public class TrainingDataCollector : MonoBehaviour
{
	private const string TrainingDataFileName = "training_sections.jsonl";
	private const int RunIdSuffixLength = 8;

	// Encoding.UTF8 writes a BOM, which Python's json module rejects with the plain utf-8 codec.
	private static readonly Encoding ExportEncoding = new UTF8Encoding(false);

	[Header("References")]
	[SerializeField] private ReportManager reportManager;

	[Header("Dataset")]
	[Tooltip("Optional anonymous ID that keeps one driver's runs in a single split. Never enter a real name.")]
	[SerializeField] private string anonymousDriverId;

	public string LastTrainingDataPath { get; private set; }

	public event Action<string> TrainingDataExported;

	private void Awake()
	{
		CacheReferences();
	}

	private void OnEnable()
	{
		CacheReferences();
		if (reportManager == null)
		{
			Debug.LogWarning("TrainingDataCollector: ReportManager reference is missing.", this);
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
	}

	private void HandleReportDataExported(string sessionDirectory)
	{
		// The session folder name links the records to driving_log.csv; the suffix keeps IDs unique across PCs.
		string runId = $"{Path.GetFileName(sessionDirectory)}_{Guid.NewGuid().ToString("N").Substring(0, RunIdSuffixLength)}";
		float sampleRateHz = 1f / Time.fixedDeltaTime;
		DrivingTrainingDatasetResult datasetResult =
			DrivingTrainingDataset.Build(reportManager.Samples, runId, anonymousDriverId, sampleRateHz);

		if (datasetResult.WrittenSectionCount == 0)
		{
			Debug.LogWarning($"TrainingDataCollector: No usable sections in {sessionDirectory} " +
				$"(skipped {datasetResult.SkippedSectionCount}).", this);
			return;
		}

		string trainingDataPath = Path.Combine(sessionDirectory, TrainingDataFileName);
		try
		{
			File.WriteAllText(trainingDataPath, datasetResult.JsonLines, ExportEncoding);
		}
		catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
		{
			Debug.LogError($"TrainingDataCollector: Failed to write {trainingDataPath}. {exception.Message}", this);
			return;
		}

		LastTrainingDataPath = trainingDataPath;
		Debug.Log($"TrainingDataCollector: Wrote {datasetResult.WrittenSectionCount} sections " +
			$"(skipped {datasetResult.SkippedSectionCount}) to {trainingDataPath}.", this);
		TrainingDataExported?.Invoke(trainingDataPath);
	}

	private void CacheReferences()
	{
		if (reportManager == null)
		{
			TryGetComponent(out reportManager);
		}
	}
}
