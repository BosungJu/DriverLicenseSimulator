using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ReportItemAutoScroll : MonoBehaviour, ISelectHandler
{
	private RectTransform itemRect;
	private ScrollRect scrollRect;

	private readonly Vector3[] worldCorners = new Vector3[4];

	private void Awake()
	{
		itemRect = transform as RectTransform;
	}

	public void OnSelect(BaseEventData eventData)
	{
		if (scrollRect == null)
		{
			scrollRect = GetComponentInParent<ScrollRect>();
		}

		if (
			itemRect == null ||
			scrollRect == null ||
			scrollRect.content == null ||
			!scrollRect.vertical
		)
		{
			return;
		}

		// 자동 정렬과 Content 크기 변경을 먼저 반영한다.
		Canvas.ForceUpdateCanvases();

		RectTransform viewport = scrollRect.viewport;

		if (viewport == null)
		{
			viewport = scrollRect.transform as RectTransform;
		}

		if (viewport == null)
		{
			return;
		}

		Bounds itemBounds = GetBoundsInViewport(
			itemRect,
			viewport
		);

		Bounds contentBounds = GetBoundsInViewport(
			scrollRect.content,
			viewport
		);

		float scrollableHeight =
			contentBounds.size.y - viewport.rect.height;

		if (scrollableHeight <= 0f)
		{
			return;
		}

		float offset;

		if (itemBounds.max.y > viewport.rect.yMax)
		{
			// 선택한 버튼이 화면 위로 벗어난 경우
			offset = itemBounds.max.y - viewport.rect.yMax;
		}
		else if (itemBounds.min.y < viewport.rect.yMin)
		{
			// 선택한 버튼이 화면 아래로 벗어난 경우
			offset = itemBounds.min.y - viewport.rect.yMin;
		}
		else
		{
			// 이미 화면 안에 있으므로 이동하지 않는다.
			return;
		}

		scrollRect.StopMovement();

		scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
			scrollRect.verticalNormalizedPosition +
			offset / scrollableHeight
		);
	}

	private Bounds GetBoundsInViewport(
		RectTransform target,
		RectTransform viewport
	)
	{
		target.GetWorldCorners(worldCorners);

		Vector3 firstCorner = viewport.InverseTransformPoint(
			worldCorners[0]
		);

		var bounds = new Bounds(firstCorner, Vector3.zero);

		for (int i = 1; i < worldCorners.Length; i++)
		{
			bounds.Encapsulate(
				viewport.InverseTransformPoint(worldCorners[i])
			);
		}

		return bounds;
	}
}