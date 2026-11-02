using System;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class ReportFileLoader : MonoBehaviour
{
	[SerializeField] private ReportUIController reportUIController;

	[Tooltip("수동 테스트에 사용할 report.json 파일의 전체 경로")]
	[SerializeField] private string reportFilePath;

	// 기존 Inspector 메뉴에서 사용하는 수동 테스트
	[ContextMenu("파일에서 보고서 불러오기")]
	public void LoadReport()
	{
		LoadReportFromPath(reportFilePath);
	}

	// 기록 버튼에서 해당 보고서의 경로를 전달할 때 사용
	public bool LoadReportFromPath(string filePath)
	{
		if (reportUIController == null)
		{
			Debug.LogError(
				"[ReportFileLoader] Report UI Controller를 연결해 주세요.",
				this
			);

			return false;
		}

		if (string.IsNullOrWhiteSpace(filePath))
		{
			Debug.LogError(
				"[ReportFileLoader] 보고서 파일 경로가 비어 있습니다.",
				this
			);

			return false;
		}

		string normalizedPath = filePath.Trim().Trim('"');

		try
		{
			if (!Path.IsPathRooted(normalizedPath))
			{
				throw new ArgumentException(
					"보고서 파일의 전체 경로가 필요합니다."
				);
			}

			string json = File.ReadAllText(
				normalizedPath,
				Encoding.UTF8
			);

			if (!reportUIController.ApplyJson(json))
			{
				return false;
			}

			Debug.Log(
				$"[ReportFileLoader] 보고서를 불러왔습니다: {normalizedPath}",
				this
			);

			return true;
		}
		catch (Exception exception) when (
			exception is IOException ||
			exception is UnauthorizedAccessException ||
			exception is ArgumentException ||
			exception is NotSupportedException
		)
		{
			Debug.LogError(
				$"[ReportFileLoader] 파일 읽기 실패: {exception.Message}",
				this
			);

			return false;
		}
	}
}