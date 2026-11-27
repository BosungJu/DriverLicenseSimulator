using UnityEngine;

// PC-local settings of the AI report pipeline. Create the asset under Assets/LocalSettings/, which .gitignore
// excludes, so the API key never reaches the repository. The asset is included in builds that reference it.
[CreateAssetMenu(fileName = "ReportPipelineSettings", menuName = "AI Report/Report Pipeline Settings")]
public class ReportPipelineSettings : ScriptableObject
{
	[Tooltip("OpenAI API key passed to report.py as OPENAI_API_KEY. When empty, the OS environment variable is used.")]
	[SerializeField] private string openAiApiKey;

	public bool HasOpenAiApiKey => !string.IsNullOrWhiteSpace(openAiApiKey);

	public string OpenAiApiKey => openAiApiKey?.Trim();
}
