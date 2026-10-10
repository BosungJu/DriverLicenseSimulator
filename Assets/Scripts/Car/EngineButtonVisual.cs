using UnityEngine;
using UnityEngine.UI;

public class EngineButtonVisual : MonoBehaviour
{
    [Header("Vehicle Systems")]
    public VehicleSystems vehicleSystems;

    [Header("Button Images")]
    public Sprite offSprite;
    public Sprite onSprite;

    private Image buttonImage;
    private bool lastEngineState;

    void Awake()
    {
        buttonImage = GetComponent<Image>();
    }

    void Start()
    {
        UpdateButtonImage(true);
    }

    void Update()
    {
        if (vehicleSystems == null || buttonImage == null)
            return;

        if (lastEngineState != vehicleSystems.EngineOn)
        {
            UpdateButtonImage(false);
        }
    }

    void UpdateButtonImage(bool forceUpdate)
    {
        if (vehicleSystems == null || buttonImage == null)
            return;

        bool engineOn = vehicleSystems.EngineOn;

        if (forceUpdate || lastEngineState != engineOn)
        {
            buttonImage.sprite = engineOn ? onSprite : offSprite;
            lastEngineState = engineOn;
        }
    }
}