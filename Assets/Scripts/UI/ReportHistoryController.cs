using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 코스 전체 주행 기록을 보고서 생성 상태(완료, 생성 중, 실패)와 함께 표시한다.
// 생성 상태는 ReportGenerationFiles가 세션 폴더의 파일에서 읽는다.
[DisallowMultipleComponent]
public class ReportHistoryController : MonoBehaviour
{
	// 생성 중인 보고서의 완료 여부를 확인하는 간격
	private const float PendingRefreshIntervalSeconds = 3f;

	private const string PendingStatusLabel = "보고서 생성 중";
	private const string FailedStatusLabel = "보고서 생성 실패";
	private const string PendingTitle = "보고서를 생성하고 있어요";
	private const string PendingDescription = "완료되면 이 화면에 자동으로 표시돼요.";
	private const string FailedTitle = "보고서를 만들지 못했어요";
	private const string FailedDescription = "자세한 원인은 콘솔 로그에서 확인할 수 있어요.";

	[Header("기록 목록")]
	[SerializeField] private Transform historyContent;
	[SerializeField] private ReportItemUI itemPrefab;

	[Tooltip("Hierarchy의 Content에 남겨 둔 예시 ReportItem")]
	[SerializeField] private GameObject exampleItem;

	[Header("보고서 파일 로더")]
	[SerializeField] private ReportFileLoader reportFileLoader;

	[Header("오른쪽 공식 기록")]
	[SerializeField] private TMP_Text reportInfoText;
	[SerializeField] private TMP_Text scoreValueText;

	[Header("상세 화면")]
	[SerializeField] private GameObject reportContent;
	[SerializeField] private GameObject emptyBox;

	[Header("보고서 상태 안내 (선택)")]
	[Tooltip("EmptyBox의 제목. 생성 중이거나 실패한 기록을 선택하면 상태 안내로 바뀐다.")]
	[SerializeField] private TMP_Text emptyTitleText;
	[Tooltip("EmptyBox의 설명. 생성 중이거나 실패한 기록을 선택하면 상태 안내로 바뀐다.")]
	[SerializeField] private TMP_Text emptyDescriptionText;

	[Header("저장 폴더")]
	[SerializeField] private string reportsFolderName = "AIReport";

	[Header("목록 탐색")]
	[SerializeField] private Button homeButton;

	private readonly List<ReportItemUI> createdItems =
		new List<ReportItemUI>();

	private readonly Dictionary<HistoryRecord, ReportItemUI> itemsByRecord =
		new Dictionary<HistoryRecord, ReportItemUI>();

	private Coroutine pendingRefreshCoroutine;
	private HistoryRecord selectedRecord;
	private string defaultEmptyTitle;
	private string defaultEmptyDescription;

	private string ReportsRootPath =>
		Path.Combine(
			Application.persistentDataPath,
			reportsFolderName
		);

	private sealed class HistoryRecord
	{
		public DateTimeOffset recordedAt;
		public int score;
		public int attemptNumber;
		public string sessionDirectory;
		public string reportPath;
		public ReportGenerationStatus status;
	}

	private void Awake()
	{
		// 상태 안내 후 되돌릴 수 있도록 씬에 입력된 문구를 보관한다.
		defaultEmptyTitle = emptyTitleText != null ? emptyTitleText.text : null;
		defaultEmptyDescription =
			emptyDescriptionText != null ? emptyDescriptionText.text : null;
	}

	private void Start()
	{
		RefreshHistory();
	}

	private void OnDisable()
	{
		StopPendingRefresh();
	}

	[ContextMenu("보고서 목록 새로고침")]
	public void RefreshHistory()
	{
		if (!Application.isPlaying)
		{
			Debug.LogWarning(
				"[ReportHistory] Play 모드에서 실행해 주세요.",
				this
			);

			return;
		}

		if (!ValidateReferences())
		{
			return;
		}

		try
		{
			List<HistoryRecord> records = ReadRecords();

			ClearCreatedItems();

			if (exampleItem != null && exampleItem.scene.IsValid())
			{
				exampleItem.SetActive(false);
			}

			reportContent.SetActive(false);
			emptyBox.SetActive(true);

			foreach (HistoryRecord record in records)
			{
				ReportItemUI item = Instantiate(
					itemPrefab,
					historyContent
				);

				item.gameObject.SetActive(true);

				BindItem(item, record);

				createdItems.Add(item);
				itemsByRecord.Add(record, item);
			}

			// 생성된 기록과 메인으로 버튼의 이동 순서를 연결한다.
			ConfigureNavigation();

            // 기록을 선택하기 전에는 EmptyBox만 표시한다.
            reportContent.SetActive(false);
            emptyBox.SetActive(true);
            SetEmptyMessage(defaultEmptyTitle, defaultEmptyDescription);

            // 생성 중인 기록이 있으면 완료나 실패로 바뀔 때까지 상태를 다시 읽는다.
            StartPendingRefresh();

            if (records.Count == 0)
            {
                Debug.Log(
                    $"[ReportHistory] 표시할 보고서가 없습니다: {ReportsRootPath}",
                    this
                );
            }
		}
		catch (Exception exception) when (
			exception is IOException ||
			exception is UnauthorizedAccessException ||
			exception is ArgumentException ||
			exception is NotSupportedException
		)
		{
			Debug.LogError(
				$"[ReportHistory] 목록 읽기 실패: {exception.Message}",
				this
			);
		}
	}

	private List<HistoryRecord> ReadRecords()
	{
		var records = new List<HistoryRecord>();

		if (!Directory.Exists(ReportsRootPath))
		{
			return records;
		}

		DateTimeOffset now = DateTimeOffset.Now;

		foreach (string directory in Directory.GetDirectories(ReportsRootPath))
		{
			string reportPath = Path.Combine(
				directory,
				ReportGenerationFiles.ReportFileName
			);

			string resultPath = Path.Combine(
				directory,
				"section_results.json"
			);

			if (!File.Exists(resultPath))
			{
				continue;
			}

			ReportGenerationStatus status =
				ReportGenerationFiles.ReadStatus(directory, now);

			// 보고서를 요청하지 않은 기록(구간 연습, 생성 꺼짐, 이전 기록)은 표시하지 않는다.
			if (status == ReportGenerationStatus.NotRequested)
			{
				continue;
			}

			try
			{
				JObject result;

				// 날짜를 자동 변환하지 않고 원래 문자열로 읽는다.
				using (var stringReader = new StringReader(
					File.ReadAllText(resultPath)
				))
				using (var jsonReader = new JsonTextReader(stringReader))
				{
					jsonReader.DateParseHandling =
						DateParseHandling.None;

					result = JObject.Load(jsonReader);
				}

				if (
					result["recordedAt"]?.Type != JTokenType.String ||
					result["score"]?.Type != JTokenType.Integer ||
					result["maxScore"]?.Type != JTokenType.Integer
				)
				{
					throw new InvalidDataException(
						"recordedAt, score, maxScore를 확인해 주세요."
					);
				}

				if (!DateTimeOffset.TryParse(
					result["recordedAt"].Value<string>(),
					CultureInfo.InvariantCulture,
					DateTimeStyles.None,
					out DateTimeOffset recordedAt
				))
				{
					throw new InvalidDataException(
						"주행 일시 형식이 올바르지 않습니다."
					);
				}

				int score = result["score"].Value<int>();
				int maxScore = result["maxScore"].Value<int>();

				if (maxScore <= 0 || score < 0 || score > maxScore)
				{
					throw new InvalidDataException(
						"공식 점수 범위가 올바르지 않습니다."
					);
				}

				records.Add(new HistoryRecord
				{
					recordedAt = recordedAt,
					score = score,
					sessionDirectory = directory,
					reportPath = reportPath,
					status = status
				});
			}
			catch (Exception exception) when (
				exception is InvalidDataException ||
				exception is IOException ||
				exception is UnauthorizedAccessException ||
				exception is JsonException ||
				exception is FormatException ||
				exception is OverflowException ||
				exception is ArgumentException
			)
			{
				Debug.LogWarning(
					$"[ReportHistory] 기록 제외: {resultPath}\n{exception.Message}",
					this
				);
			}
		}

		// 오래된 기록부터 정렬해 날짜별 회차를 계산한다.
		records.Sort((left, right) =>
		{
			int comparison = left.recordedAt.CompareTo(
				right.recordedAt
			);

			return comparison != 0
				? comparison
				: StringComparer.Ordinal.Compare(
					left.reportPath,
					right.reportPath
				);
		});

		var dailyCounts = new Dictionary<string, int>();

		foreach (HistoryRecord record in records)
		{
			string day = record.recordedAt.ToString(
				"yyyy-MM-dd",
				CultureInfo.InvariantCulture
			);

			dailyCounts.TryGetValue(day, out int count);

			record.attemptNumber = count + 1;
			dailyCounts[day] = record.attemptNumber;
		}

		// 화면에는 최신 기록부터 표시한다.
		records.Reverse();

		return records;
	}

	private void ShowReport(HistoryRecord record)
	{
		selectedRecord = record;

		reportContent.SetActive(false);
		emptyBox.SetActive(true);

		// 보고서가 없는 기록은 EmptyBox에 생성 상태를 안내한다.
		if (record.status == ReportGenerationStatus.Pending)
		{
			SetEmptyMessage(PendingTitle, PendingDescription);
			return;
		}

		if (record.status == ReportGenerationStatus.Failed)
		{
			SetEmptyMessage(FailedTitle, FailedDescription);
			LogGenerationFailure(record);
			return;
		}

		SetEmptyMessage(defaultEmptyTitle, defaultEmptyDescription);

		if (!reportFileLoader.LoadReportFromPath(record.reportPath))
		{
			return;
		}

		string date = record.recordedAt.ToString(
			"yyyy.MM.dd",
			CultureInfo.InvariantCulture
		);

		reportInfoText.text =
			$"{date}  |  주행 연습 {record.attemptNumber}회차";

		scoreValueText.text = $"{record.score}점";
	}

	private void BindItem(ReportItemUI item, HistoryRecord record)
	{
		string date = record.recordedAt.ToString(
			"yyyy.MM.dd",
			CultureInfo.InvariantCulture
		);

		item.Bind(
			date,
			record.attemptNumber,
			record.score,
			() => ShowReport(record),
			GetStatusLabel(record.status)
		);
	}

	private static string GetStatusLabel(ReportGenerationStatus status)
	{
		switch (status)
		{
			case ReportGenerationStatus.Pending:
				return PendingStatusLabel;

			case ReportGenerationStatus.Failed:
				return FailedStatusLabel;

			default:
				return null;
		}
	}

	private void SetEmptyMessage(string title, string description)
	{
		if (emptyTitleText != null && title != null)
		{
			emptyTitleText.text = title;
		}

		if (emptyDescriptionText != null && description != null)
		{
			emptyDescriptionText.text = description;
		}
	}

	private void LogGenerationFailure(HistoryRecord record)
	{
		if (ReportGenerationFiles.TryReadError(
			record.sessionDirectory,
			out ReportErrorFile errorFile
		))
		{
			Debug.LogWarning(
				$"[ReportHistory] 보고서 생성 실패: {record.sessionDirectory}\n" +
				$"{errorFile.stage}: {errorFile.message}",
				this
			);

			return;
		}

		Debug.LogWarning(
			$"[ReportHistory] 제한 시간 안에 보고서 결과가 기록되지 않았습니다: {record.sessionDirectory}",
			this
		);
	}

	private void StartPendingRefresh()
	{
		StopPendingRefresh();

		if (HasPendingRecord())
		{
			pendingRefreshCoroutine = StartCoroutine(RefreshPendingRecords());
		}
	}

	private void StopPendingRefresh()
	{
		if (pendingRefreshCoroutine == null)
		{
			return;
		}

		StopCoroutine(pendingRefreshCoroutine);
		pendingRefreshCoroutine = null;
	}

	// 생성 중인 기록은 완료되거나, 제한 시간이 지나 실패로 바뀌면 끝난다.
	private IEnumerator RefreshPendingRecords()
	{
		while (HasPendingRecord())
		{
			yield return new WaitForSecondsRealtime(PendingRefreshIntervalSeconds);

			UpdatePendingRecords();
		}

		pendingRefreshCoroutine = null;
	}

	private bool HasPendingRecord()
	{
		foreach (HistoryRecord record in itemsByRecord.Keys)
		{
			if (record.status == ReportGenerationStatus.Pending)
			{
				return true;
			}
		}

		return false;
	}

	// 목록을 다시 만들지 않고 바뀐 기록만 갱신해 현재 선택과 탐색 상태를 유지한다.
	private void UpdatePendingRecords()
	{
		DateTimeOffset now = DateTimeOffset.Now;
		var changedRecords = new List<HistoryRecord>();

		foreach (HistoryRecord record in itemsByRecord.Keys)
		{
			if (record.status != ReportGenerationStatus.Pending)
			{
				continue;
			}

			ReportGenerationStatus currentStatus =
				ReportGenerationFiles.ReadStatus(record.sessionDirectory, now);

			if (currentStatus != record.status)
			{
				record.status = currentStatus;
				changedRecords.Add(record);
			}
		}

		foreach (HistoryRecord record in changedRecords)
		{
			ReportItemUI item = itemsByRecord[record];

			if (item != null)
			{
				BindItem(item, record);
			}

			if (record == selectedRecord)
			{
				ShowReport(record);
			}
		}
	}

	private void ClearCreatedItems()
	{
		StopPendingRefresh();

		foreach (ReportItemUI item in createdItems)
		{
			if (item != null)
			{
				item.gameObject.SetActive(false);
				Destroy(item.gameObject);
			}
		}

		createdItems.Clear();
		itemsByRecord.Clear();
		selectedRecord = null;
	}

	private void ConfigureNavigation()
	{
		if (homeButton == null)
		{
			Debug.LogError(
				"[ReportHistory] Home Button을 연결해 주세요.",
				this
			);

			return;
		}

		var buttons = new List<Button>();

		foreach (ReportItemUI item in createdItems)
		{
			if (item == null)
			{
				continue;
			}

			Button button = item.GetComponent<Button>();

			if (
				button != null &&
				button.isActiveAndEnabled &&
				button.IsInteractable()
			)
			{
				buttons.Add(button);
			}
		}

		for (int i = 0; i < buttons.Count; i++)
		{
			var navigation = new Navigation
			{
				mode = Navigation.Mode.Explicit,

				// 첫 기록에서 위로 이동하면 메인으로 버튼을 선택한다.
				selectOnUp = i > 0
					? buttons[i - 1]
					: homeButton,

				// 마지막 기록에서 아래로 이동하면 메인으로 버튼을 선택한다.
				selectOnDown = i < buttons.Count - 1
					? buttons[i + 1]
					: homeButton
			};

			buttons[i].navigation = navigation;
		}

		var homeNavigation = new Navigation
		{
			mode = Navigation.Mode.Explicit,

			// 메인으로 버튼에서 위로 이동하면 마지막 기록을 선택한다.
			selectOnUp = buttons.Count > 0
				? buttons[buttons.Count - 1]
				: null,

			// 메인으로 버튼에서 아래로 이동하면 첫 기록을 선택한다.
			selectOnDown = buttons.Count > 0
				? buttons[0]
				: null
		};

		homeButton.navigation = homeNavigation;
	}

	private bool ValidateReferences()
	{
		if (
			historyContent == null ||
			itemPrefab == null ||
			reportFileLoader == null ||
			reportInfoText == null ||
			scoreValueText == null ||
			reportContent == null ||
			emptyBox == null
		)
		{
			Debug.LogError(
				"[ReportHistory] Inspector의 필수 항목을 연결해 주세요.",
				this
			);

			return false;
		}

		return true;
	}

	[ContextMenu("저장 폴더 경로 확인")]
	private void ShowReportsFolderPath()
	{
		Directory.CreateDirectory(ReportsRootPath);

		Debug.Log(
			$"[ReportHistory] 저장 폴더: {ReportsRootPath}",
			this
		);
	}
}