using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(VehicleSystems), typeof(CarController), typeof(Rigidbody))]
public class VehicleDashboardHud : MonoBehaviour
{
	private const string DashboardSpriteFolder = "UI/Car/";

	private static readonly Color ActiveGreen = new Color32(85, 237, 134, 255);
	private static readonly Color ActiveAmber = new Color32(255, 180, 84, 255);
	private static readonly Color ActiveBlue = new Color32(105, 184, 255, 255);
	private static readonly Color InactiveColor = new Color32(146, 154, 166, 56);
	private static readonly Color GaugeArcColor = new Color32(105, 184, 255, 200);
	private static readonly Color NeedleColor = new Color32(255, 96, 72, 255);
	private static readonly Color SpeedTextColor = Color.white;
	private static readonly Color UnitTextColor = new Color32(146, 154, 166, 255);

	private const float ReferenceWidth = 1280f;
	private const float ReferenceHeight = 720f;
	private const float PanelWidth = 720f;
	private const float BackgroundAspectRatio = 2172f / 724f; // Sprite_Dashboard_Background 원본 비율
	private const float PanelMargin = 12f;
	private const float MetersPerSecondToKmh = 3.6f;

	// 속도계 눈금: 1단 최고 속도 제한(110km/h)이 바늘 끝에 붙지 않도록 조금 넓게 잡는다.
	private const float GaugeMaxSpeedKmh = 120f;
	// Sprite_Dashboard_GaugeArc 원호의 양 끝 각도 (바늘 스프라이트가 위쪽 0°, 시계 방향 +)
	private const float NeedleMinAngle = -135f;
	private const float NeedleMaxAngle = 135f;

	// 배경 스프라이트 안의 배치 (패널 기준 0~1, 좌상단 원점)
	private static readonly Vector2 SpeedPodCenter = new Vector2(0.145f, 0.49f);
	private static readonly Vector2 GearPodCenter = new Vector2(0.855f, 0.49f);
	private const float GaugeSizeRatio = 0.62f; // 패널 높이 대비 게이지 크기
	private const float SignalRowY = 0.2f;
	private const float LeftSignalX = 0.36f;
	private const float RightSignalX = 0.64f;
	private const float SignalIconSize = 34f;
	private const float StatusRowY = 0.58f;
	private const float StatusRowStartX = 0.33f;
	private const float StatusRowEndX = 0.67f;
	private const float StatusIconSize = 28f;
	private const int StatusIconCount = 8;
	private const float GearLetterSpacing = 30f;

	private static readonly VehicleGear[] GearOrder = { VehicleGear.Park, VehicleGear.Reverse, VehicleGear.Neutral, VehicleGear.Drive };
	private static readonly string[] GearLetters = { "P", "R", "N", "D" };

	[Header("모델 계기판")]
	[SerializeField] private Canvas dashboardCanvas;
	[SerializeField] private bool showScreenOverlay = true;
	[Tooltip("계기판 UI를 모델 화면 표면에서 운전자 쪽으로 띄우는 거리(m). 0이면 표면과 겹쳐 Z-fighting으로 깜빡인다.")]
	[SerializeField, Min(0f)] private float surfaceOffsetMeters = 0.002f;

	private RectTransform dashboardRoot;
	private RectTransform worldNeedle;
	private Text worldSpeed;
	private Text[] worldGears;
	private RawImage worldLeftSignal;
	private RawImage worldRightSignal;
	private RawImage[] worldStatusIcons;

	private VehicleSystems vehicleSystems;
	private CarController carController;
	private Rigidbody vehicleBody;
	private WiperController wiperController;

	private Texture2D backgroundTexture;
	private Texture2D gaugeArcTexture;
	private Texture2D gaugeNeedleTexture;
	private Texture2D leftSignalTexture;
	private Texture2D rightSignalTexture;
	private Texture2D engineTexture;
	private Texture2D seatBeltTexture;
	private Texture2D parkingBrakeTexture;
	private Texture2D lowBeamTexture;
	private Texture2D highBeamTexture;
	private Texture2D hazardTexture;
	private Texture2D hornTexture;
	private Texture2D wiperTexture;

	private GUIStyle speedStyle;
	private GUIStyle unitStyle;
	private GUIStyle gearStyle;
	private int displayedSpeed = -1;
	private string speedLabel;
	private float needleAngle = NeedleMinAngle;

	private void Awake()
	{
		vehicleSystems = GetComponent<VehicleSystems>();
		carController = GetComponent<CarController>();
		vehicleBody = GetComponent<Rigidbody>();
		// 와이퍼는 차량 하위에 없을 수도 있으므로 없으면 꺼진 상태로 표시한다.
		wiperController = GetComponentInChildren<WiperController>(true);

		LoadDashboardTextures();
		AssignDashboardCanvas();
		CreateWorldDashboard();
	}

	private void LateUpdate()
	{
		if (vehicleBody == null)
		{
			return;
		}

		float speedKmh = vehicleBody.linearVelocity.magnitude * MetersPerSecondToKmh;
		needleAngle = Mathf.Lerp(NeedleMinAngle, NeedleMaxAngle, Mathf.Clamp01(speedKmh / GaugeMaxSpeedKmh));

		int roundedSpeedKmh = Mathf.RoundToInt(speedKmh);
		if (speedLabel == null || roundedSpeedKmh != displayedSpeed)
		{
			displayedSpeed = roundedSpeedKmh;
			speedLabel = roundedSpeedKmh.ToString();
		}

		UpdateWorldDashboard();
	}

	private void OnEnable()
	{
		if (dashboardRoot != null)
		{
			dashboardRoot.gameObject.SetActive(true);
		}
	}

	private void OnDisable()
	{
		if (dashboardRoot != null)
		{
			dashboardRoot.gameObject.SetActive(false);
		}
	}

	private void OnDestroy()
	{
		if (dashboardRoot != null)
		{
			Destroy(dashboardRoot.gameObject);
		}
	}

	private void OnGUI()
	{
		if (!showScreenOverlay || vehicleSystems == null || carController == null || vehicleBody == null)
		{
			return;
		}

		float scale = Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight);
		if (scale <= 0f)
		{
			return;
		}

		EnsureTextStyles();

		Matrix4x4 previousMatrix = GUI.matrix;
		Color previousColor = GUI.color;
		GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);

		float panelHeight = PanelWidth / BackgroundAspectRatio;
		Rect panelRect = new Rect(
			(Screen.width / scale - PanelWidth) * 0.5f,
			Screen.height / scale - panelHeight - PanelMargin,
			PanelWidth,
			panelHeight);

		DrawTinted(panelRect, backgroundTexture, Color.white);
		DrawSpeedGauge(panelRect);
		DrawGearSelector(panelRect);
		DrawSignals(panelRect);
		DrawStatusIcons(panelRect);

		GUI.color = previousColor;
		GUI.matrix = previousMatrix;
	}

	private void AssignDashboardCanvas()
	{
		if (dashboardCanvas != null)
		{
			return;
		}

		foreach (Canvas candidate in GetComponentsInChildren<Canvas>(true))
		{
			if (candidate.renderMode != RenderMode.WorldSpace)
			{
				continue;
			}

			for (Transform ancestor = candidate.transform; ancestor != null && ancestor != transform; ancestor = ancestor.parent)
			{
				if (ancestor.name.StartsWith("Display_InstrumentCluster", System.StringComparison.Ordinal))
				{
					dashboardCanvas = candidate;
					return;
				}
			}
		}
	}

	private void CreateWorldDashboard()
	{
		if (dashboardCanvas == null)
		{
			if (!showScreenOverlay)
			{
				Debug.LogWarning("[VehicleDashboardHud] Instrument Canvas not found. Assign Dashboard Canvas in the Inspector.", this);
			}
			return;
		}

		if (dashboardCanvas.renderMode != RenderMode.WorldSpace)
		{
			Debug.LogWarning("[VehicleDashboardHud] Dashboard Canvas must use World Space.", this);
			return;
		}

		dashboardRoot = CreateWorldRect("Dashboard", dashboardCanvas.transform);
		float panelHeight = PanelWidth / BackgroundAspectRatio;
		dashboardRoot.sizeDelta = new Vector2(PanelWidth, panelHeight);
		ApplySurfaceOffset();
		Rect panelRect = new Rect(0f, 0f, PanelWidth, panelHeight);

		CreateWorldImage("Background", backgroundTexture, Color.white, panelRect);
		float gaugeSize = panelHeight * GaugeSizeRatio;
		Vector2 gaugeCenter = GetPanelPoint(panelRect, SpeedPodCenter);
		Rect gaugeRect = GetCenteredRect(gaugeCenter, gaugeSize, gaugeSize);
		CreateWorldImage("GaugeArc", gaugeArcTexture, GaugeArcColor, gaugeRect);
		worldNeedle = CreateWorldImage("GaugeNeedle", gaugeNeedleTexture, NeedleColor, gaugeRect).rectTransform;
		worldSpeed = CreateWorldText("Speed", "0", 34, SpeedTextColor,
			GetCenteredRect(gaugeCenter + new Vector2(0f, gaugeSize * 0.22f), gaugeSize, 40f));
		CreateWorldText("Unit", "km/h", 12, UnitTextColor,
			GetCenteredRect(gaugeCenter + new Vector2(0f, gaugeSize * 0.4f), gaugeSize, 16f));

		worldGears = new Text[GearOrder.Length];
		Vector2 gearCenter = GetPanelPoint(panelRect, GearPodCenter);
		float firstLetterOffset = -GearLetterSpacing * (GearOrder.Length - 1) * 0.5f;
		for (int i = 0; i < GearOrder.Length; i++)
		{
			Vector2 letterCenter = gearCenter + new Vector2(firstLetterOffset + GearLetterSpacing * i, 0f);
			worldGears[i] = CreateWorldText("Gear_" + GearLetters[i], GearLetters[i], 24, InactiveColor,
				GetCenteredRect(letterCenter, GearLetterSpacing, 40f));
		}

		worldLeftSignal = CreateWorldImage("LeftSignal", leftSignalTexture, InactiveColor,
			GetCenteredRect(GetPanelPoint(panelRect, new Vector2(LeftSignalX, SignalRowY)), SignalIconSize, SignalIconSize));
		worldRightSignal = CreateWorldImage("RightSignal", rightSignalTexture, InactiveColor,
			GetCenteredRect(GetPanelPoint(panelRect, new Vector2(RightSignalX, SignalRowY)), SignalIconSize, SignalIconSize));

		Texture2D[] statusTextures = { engineTexture, seatBeltTexture, parkingBrakeTexture, lowBeamTexture,
			highBeamTexture, hazardTexture, hornTexture, wiperTexture };
		worldStatusIcons = new RawImage[StatusIconCount];
		for (int i = 0; i < StatusIconCount; i++)
		{
			float iconX = Mathf.Lerp(StatusRowStartX, StatusRowEndX, (float)i / (StatusIconCount - 1));
			worldStatusIcons[i] = CreateWorldImage("Status_" + i, statusTextures[i], InactiveColor,
				GetCenteredRect(GetPanelPoint(panelRect, new Vector2(iconX, StatusRowY)), StatusIconSize, StatusIconSize));
		}

		UpdateWorldDashboard();
		dashboardRoot.gameObject.SetActive(isActiveAndEnabled);
	}

	// UI 그래픽은 캔버스 -Z 쪽(운전자 방향)을 향하므로 그쪽으로 띄워 표시 메쉬 앞에 그려지게 한다.
	// 캔버스 부모(FBX 노드)의 스케일과 무관하게 미터 단위가 되도록 월드 좌표에서 계산한다.
	private void ApplySurfaceOffset()
	{
		Transform canvasTransform = dashboardCanvas.transform;
		Vector3 offsetWorldPosition = canvasTransform.position - canvasTransform.forward * surfaceOffsetMeters;
		dashboardRoot.localPosition = canvasTransform.InverseTransformPoint(offsetWorldPosition);
	}

	private void UpdateWorldDashboard()
	{
		if (dashboardRoot == null || dashboardCanvas == null || vehicleSystems == null || carController == null)
		{
			return;
		}

		// Canvas의 월드 크기를 유지하면서 자식 UI는 픽셀 단위로 배치한다.
		Rect canvasRect = ((RectTransform)dashboardCanvas.transform).rect;
		float scale = Mathf.Min(canvasRect.width / PanelWidth, canvasRect.height / (PanelWidth / BackgroundAspectRatio));
		dashboardRoot.localScale = new Vector3(scale, scale, 1f);
		worldNeedle.localRotation = Quaternion.Euler(0f, 0f, -needleAngle);
		worldSpeed.text = speedLabel ?? "0";
		for (int i = 0; i < GearOrder.Length; i++)
		{
			worldGears[i].color = carController.CurrentGear == GearOrder[i] ? ActiveBlue : InactiveColor;
		}

		worldLeftSignal.color = vehicleSystems.LeftSignalVisible ? ActiveGreen : InactiveColor;
		worldRightSignal.color = vehicleSystems.RightSignalVisible ? ActiveGreen : InactiveColor;
		worldStatusIcons[0].color = vehicleSystems.EngineOn ? ActiveGreen : InactiveColor;
		worldStatusIcons[1].color = vehicleSystems.SeatBeltOn ? ActiveGreen : InactiveColor;
		worldStatusIcons[2].color = carController.ParkingBrakeOn ? ActiveAmber : InactiveColor;
		worldStatusIcons[3].color = vehicleSystems.HeadlightOn && !vehicleSystems.HighBeamOn ? ActiveGreen : InactiveColor;
		worldStatusIcons[4].color = vehicleSystems.HighBeamOn ? ActiveBlue : InactiveColor;
		worldStatusIcons[5].color = vehicleSystems.HazardOn ? ActiveAmber : InactiveColor;
		worldStatusIcons[6].color = vehicleSystems.HornOn ? ActiveAmber : InactiveColor;
		worldStatusIcons[7].color = wiperController != null && wiperController.IsWiping ? ActiveGreen : InactiveColor;
	}

	private static RectTransform CreateWorldRect(string objectName, Transform parent)
	{
		var uiObject = new GameObject(objectName, typeof(RectTransform));
		uiObject.layer = parent.gameObject.layer;
		var rect = (RectTransform)uiObject.transform;
		rect.SetParent(parent, false);
		rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
		return rect;
	}

	private RectTransform CreateWorldElement(string objectName, Rect bounds)
	{
		RectTransform rect = CreateWorldRect(objectName, dashboardRoot);
		rect.sizeDelta = bounds.size;
		// IMGUI 원점은 좌상단, RectTransform의 Y축은 위쪽이다.
		rect.anchoredPosition = new Vector2(bounds.center.x - PanelWidth * 0.5f,
			PanelWidth / BackgroundAspectRatio * 0.5f - bounds.center.y);
		return rect;
	}

	private RawImage CreateWorldImage(string objectName, Texture2D texture, Color tint, Rect bounds)
	{
		if (texture != null)
		{
			float fitScale = Mathf.Min(bounds.width / texture.width, bounds.height / texture.height);
			bounds = GetCenteredRect(bounds.center, texture.width * fitScale, texture.height * fitScale);
		}

		RectTransform rect = CreateWorldElement(objectName, bounds);
		var image = rect.gameObject.AddComponent<RawImage>();
		image.texture = texture;
		image.color = tint;
		image.raycastTarget = false;
		image.enabled = texture != null;
		return image;
	}

	private Text CreateWorldText(string objectName, string label, int fontSize, Color tint, Rect bounds)
	{
		RectTransform rect = CreateWorldElement(objectName, bounds);
		var text = rect.gameObject.AddComponent<Text>();
		text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		text.fontSize = fontSize;
		text.fontStyle = FontStyle.Bold;
		text.alignment = TextAnchor.MiddleCenter;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.color = tint;
		text.text = label;
		text.raycastTarget = false;
		return text;
	}

	private void LoadDashboardTextures()
	{
		backgroundTexture = LoadDashboardTexture("Sprite_Dashboard_Background");
		gaugeArcTexture = LoadDashboardTexture("Sprite_Dashboard_GaugeArc");
		gaugeNeedleTexture = LoadDashboardTexture("Sprite_Dashboard_GaugeNeedle");
		leftSignalTexture = LoadDashboardTexture("Sprite_Dashboard_LeftSignal");
		rightSignalTexture = LoadDashboardTexture("Sprite_Dashboard_RightSignal");
		engineTexture = LoadDashboardTexture("Sprite_Dashboard_Engine");
		seatBeltTexture = LoadDashboardTexture("Sprite_Dashboard_SeatBelt");
		parkingBrakeTexture = LoadDashboardTexture("Sprite_Dashboard_ParkingBrake");
		lowBeamTexture = LoadDashboardTexture("Sprite_Dashboard_LowBeam");
		highBeamTexture = LoadDashboardTexture("Sprite_Dashboard_HighBeam");
		hazardTexture = LoadDashboardTexture("Sprite_Dashboard_Hazard");
		hornTexture = LoadDashboardTexture("Sprite_Dashboard_Horn");
		wiperTexture = LoadDashboardTexture("Sprite_Dashboard_Wiper");
	}

	private Texture2D LoadDashboardTexture(string spriteName)
	{
		Texture2D dashboardTexture = Resources.Load<Texture2D>(DashboardSpriteFolder + spriteName);
		if (dashboardTexture == null)
		{
			Debug.LogWarning($"[VehicleDashboardHud] Missing dashboard sprite: Resources/{DashboardSpriteFolder}{spriteName}", this);
		}

		return dashboardTexture;
	}

	private void EnsureTextStyles()
	{
		if (speedStyle != null)
		{
			return;
		}

		speedStyle = new GUIStyle(GUI.skin.label)
		{
			fontSize = 34,
			fontStyle = FontStyle.Bold,
			alignment = TextAnchor.MiddleCenter
		};
		speedStyle.normal.textColor = SpeedTextColor;

		unitStyle = new GUIStyle(speedStyle)
		{
			fontSize = 12,
			fontStyle = FontStyle.Normal
		};
		unitStyle.normal.textColor = UnitTextColor;

		gearStyle = new GUIStyle(speedStyle) { fontSize = 24 };
	}

	private void DrawSpeedGauge(Rect panelRect)
	{
		float gaugeSize = panelRect.height * GaugeSizeRatio;
		Vector2 gaugeCenter = GetPanelPoint(panelRect, SpeedPodCenter);
		Rect gaugeRect = GetCenteredRect(gaugeCenter, gaugeSize, gaugeSize);

		DrawTinted(gaugeRect, gaugeArcTexture, GaugeArcColor);

		// 스케일이 들어간 GUI.matrix에서도 게이지 중심을 축으로 돌도록 로컬 좌표에서 회전을 합성한다.
		Matrix4x4 gaugeMatrix = GUI.matrix;
		GUI.matrix = gaugeMatrix
			* Matrix4x4.Translate(gaugeCenter)
			* Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, needleAngle))
			* Matrix4x4.Translate(-gaugeCenter);
		DrawTinted(gaugeRect, gaugeNeedleTexture, NeedleColor);
		GUI.matrix = gaugeMatrix;

		GUI.color = Color.white;
		GUI.Label(GetCenteredRect(gaugeCenter + new Vector2(0f, gaugeSize * 0.22f), gaugeSize, 40f), speedLabel ?? string.Empty, speedStyle);
		GUI.Label(GetCenteredRect(gaugeCenter + new Vector2(0f, gaugeSize * 0.4f), gaugeSize, 16f), "km/h", unitStyle);
	}

	private void DrawGearSelector(Rect panelRect)
	{
		Vector2 gearCenter = GetPanelPoint(panelRect, GearPodCenter);
		float firstLetterOffset = -GearLetterSpacing * (GearOrder.Length - 1) * 0.5f;
		VehicleGear currentGear = carController.CurrentGear;

		for (int i = 0; i < GearOrder.Length; i++)
		{
			bool isCurrentGear = GearOrder[i] == currentGear;
			Vector2 letterCenter = gearCenter + new Vector2(firstLetterOffset + GearLetterSpacing * i, 0f);

			GUI.color = isCurrentGear ? ActiveBlue : InactiveColor;
			GUI.Label(GetCenteredRect(letterCenter, GearLetterSpacing, 40f), GearLetters[i], gearStyle);
		}
	}

	private void DrawSignals(Rect panelRect)
	{
		Vector2 leftSignalCenter = GetPanelPoint(panelRect, new Vector2(LeftSignalX, SignalRowY));
		Vector2 rightSignalCenter = GetPanelPoint(panelRect, new Vector2(RightSignalX, SignalRowY));

		DrawIndicator(leftSignalCenter, SignalIconSize, leftSignalTexture, vehicleSystems.LeftSignalVisible, ActiveGreen);
		DrawIndicator(rightSignalCenter, SignalIconSize, rightSignalTexture, vehicleSystems.RightSignalVisible, ActiveGreen);
	}

	private void DrawStatusIcons(Rect panelRect)
	{
		bool isLowBeamOn = vehicleSystems.HeadlightOn && !vehicleSystems.HighBeamOn;
		bool isWiping = wiperController != null && wiperController.IsWiping;

		DrawStatusIcon(panelRect, 0, engineTexture, vehicleSystems.EngineOn, ActiveGreen);
		DrawStatusIcon(panelRect, 1, seatBeltTexture, vehicleSystems.SeatBeltOn, ActiveGreen);
		DrawStatusIcon(panelRect, 2, parkingBrakeTexture, carController.ParkingBrakeOn, ActiveAmber);
		DrawStatusIcon(panelRect, 3, lowBeamTexture, isLowBeamOn, ActiveGreen);
		DrawStatusIcon(panelRect, 4, highBeamTexture, vehicleSystems.HighBeamOn, ActiveBlue);
		DrawStatusIcon(panelRect, 5, hazardTexture, vehicleSystems.HazardOn, ActiveAmber);
		DrawStatusIcon(panelRect, 6, hornTexture, vehicleSystems.HornOn, ActiveAmber);
		DrawStatusIcon(panelRect, 7, wiperTexture, isWiping, ActiveGreen);
	}

	private void DrawStatusIcon(Rect panelRect, int slotIndex, Texture2D iconTexture, bool isActive, Color activeColor)
	{
		float slotRatio = (float)slotIndex / (StatusIconCount - 1);
		float iconX = Mathf.Lerp(StatusRowStartX, StatusRowEndX, slotRatio);
		Vector2 iconCenter = GetPanelPoint(panelRect, new Vector2(iconX, StatusRowY));

		DrawIndicator(iconCenter, StatusIconSize, iconTexture, isActive, activeColor);
	}

	private void DrawIndicator(Vector2 center, float size, Texture2D iconTexture, bool isActive, Color activeColor)
	{
		DrawTinted(GetCenteredRect(center, size, size), iconTexture, isActive ? activeColor : InactiveColor);
	}

	private static void DrawTinted(Rect bounds, Texture2D texture, Color tint)
	{
		if (texture == null)
		{
			return;
		}

		GUI.color = tint;
		GUI.DrawTexture(bounds, texture, ScaleMode.ScaleToFit, true);
	}

	private static Vector2 GetPanelPoint(Rect panelRect, Vector2 normalizedPoint)
	{
		return new Vector2(
			panelRect.x + panelRect.width * normalizedPoint.x,
			panelRect.y + panelRect.height * normalizedPoint.y);
	}

	private static Rect GetCenteredRect(Vector2 center, float width, float height)
	{
		return new Rect(center.x - width * 0.5f, center.y - height * 0.5f, width, height);
	}
}
