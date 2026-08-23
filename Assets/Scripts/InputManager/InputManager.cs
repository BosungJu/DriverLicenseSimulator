using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

public class InputManager : MonoBehaviour
{
    public enum PinType
    {
        LeftTurnSignal = 2,
        RightTurnSignal = 3,
        EngineButton = 4,
        EmergencyButton = 5,
        FrontLightButton = 6,
        HighBeamButton = 7,
        WiperButton = 8,
        EmergencyLed = 9,
        EngineLed = 10
    }

    [Serializable]
    public class SerialMessageEvent : UnityEvent<string>
    {
    }

    [Serializable]
    public class PinStateEvent : UnityEvent<PinType, bool>
    {
    }

    const int ReadTimeoutMilliseconds = 100;
    const int ThreadJoinTimeoutMilliseconds = 500;
    const int MaxMessagesPerFrame = 100;

    static readonly Dictionary<string, PinType> CommandPinTypes = new Dictionary<string, PinType>(StringComparer.OrdinalIgnoreCase)
    {
        { "LeftTurnSignalButton", PinType.LeftTurnSignal },
        { "RightTurnSignalButton", PinType.RightTurnSignal },
        { "EngineButton", PinType.EngineButton },
        { "EmergencyButton", PinType.EmergencyButton },
        { "FrontLightButton", PinType.FrontLightButton },
        { "HighBeamButton", PinType.HighBeamButton },
        { "WiperButton", PinType.WiperButton }
    };

    [Header("Serial Port")]
    [SerializeField] string portName = "COM3";
    [SerializeField] int baudRate = 115200;
    [SerializeField] bool autoConnect = true;
    [SerializeField] bool dtrEnable = true;

    [Header("Events")]
    [SerializeField] SerialMessageEvent serialMessageReceived = new SerialMessageEvent();
    [SerializeField] PinStateEvent pinStateChanged = new PinStateEvent();

    readonly ConcurrentQueue<string> receivedMessages = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> serialErrors = new ConcurrentQueue<string>();
    readonly Dictionary<PinType, bool> pinStates = new Dictionary<PinType, bool>();

    SerialPort serialPort;
    Thread readThread;
    volatile bool isReading;
    volatile bool disconnectRequested;

    public bool IsConnected => serialPort != null && serialPort.IsOpen;
    public string LatestMessage { get; private set; }

    public bool LeftTurnSignal => GetPinState(PinType.LeftTurnSignal);
    public bool RightTurnSignal => GetPinState(PinType.RightTurnSignal);
    public bool EngineButton => GetPinState(PinType.EngineButton);
    public bool EmergencyButton => GetPinState(PinType.EmergencyButton);
    public bool FrontLightButton => GetPinState(PinType.FrontLightButton);
    public bool HighBeamButton => GetPinState(PinType.HighBeamButton);
    public bool WiperButton => GetPinState(PinType.WiperButton);

    public event Action<string> MessageReceived;
    public event Action<PinType, bool> PinStateChanged;

    void OnEnable()
    {
        if (autoConnect)
        {
            Connect();
        }
    }

    void Update()
    {
        int processedMessageCount = 0;
        while (processedMessageCount < MaxMessagesPerFrame && receivedMessages.TryDequeue(out string message))
        {
            ProcessMessage(message);
            processedMessageCount++;
        }

        while (serialErrors.TryDequeue(out string error))
        {
            Debug.LogError($"[InputManager] Serial port error: {error}", this);
        }

        if (disconnectRequested)
        {
            Disconnect();
        }
    }

    void OnDisable()
    {
        Disconnect();
    }

    void OnApplicationQuit()
    {
        Disconnect();
    }

    public void Connect()
    {
        if (IsConnected)
        {
            return;
        }

        Disconnect();

        if (string.IsNullOrWhiteSpace(portName))
        {
            Debug.LogError("[InputManager] Serial port name is empty.", this);
            return;
        }

        SerialPort openedPort = null;

        try
        {
            openedPort = new SerialPort(portName, baudRate)
            {
                DtrEnable = dtrEnable,
                NewLine = "\n",
                ReadTimeout = ReadTimeoutMilliseconds
            };
            openedPort.Open();
        }
        catch (Exception exception) when (
            exception is ArgumentException ||
            exception is IOException ||
            exception is InvalidOperationException ||
            exception is UnauthorizedAccessException)
        {
            openedPort?.Dispose();
            Debug.LogError($"[InputManager] Could not open {portName}: {exception.Message}", this);
            return;
        }

        serialPort = openedPort;
        disconnectRequested = false;
        isReading = true;
        readThread = new Thread(() => ReadSerialPort(openedPort))
        {
            IsBackground = true,
            Name = "Arduino Serial Reader"
        };
        readThread.Start();

        Debug.Log($"[InputManager] Connected to {portName} at {baudRate} baud.", this);
    }

    public void Disconnect()
    {
        isReading = false;
        disconnectRequested = false;

        SerialPort openedPort = serialPort;
        serialPort = null;

        if (openedPort != null)
        {
            try
            {
                if (openedPort.IsOpen)
                {
                    openedPort.Close();
                }
            }
            catch (IOException exception)
            {
                Debug.LogWarning($"[InputManager] Error while closing serial port: {exception.Message}", this);
            }
            finally
            {
                openedPort.Dispose();
            }
        }

        if (readThread != null && readThread.IsAlive)
        {
            readThread.Join(ThreadJoinTimeoutMilliseconds);
        }

        readThread = null;
    }

    public bool GetPinState(PinType pinType)
    {
        return pinStates.TryGetValue(pinType, out bool state) && state;
    }

    void ReadSerialPort(SerialPort openedPort)
    {
        while (isReading && openedPort.IsOpen)
        {
            try
            {
                string message = openedPort.ReadLine().TrimEnd('\r');
                receivedMessages.Enqueue(message);
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is InvalidOperationException ||
                exception is UnauthorizedAccessException)
            {
                if (isReading)
                {
                    serialErrors.Enqueue(exception.Message);
                    disconnectRequested = true;
                }

                break;
            }
        }
    }

    void ProcessMessage(string message)
    {
        LatestMessage = message;
        Debug.Log($"[InputManager] Received: {message}", this);
        serialMessageReceived.Invoke(message);
        MessageReceived?.Invoke(message);

        if (!TryParseCommandState(message, out PinType pinType, out bool state))
        {
            return;
        }

        bool stateChanged = !pinStates.TryGetValue(pinType, out bool previousState) || previousState != state;
        pinStates[pinType] = state;

        if (stateChanged)
        {
            pinStateChanged.Invoke(pinType, state);
            PinStateChanged?.Invoke(pinType, state);
        }
    }

    static bool TryParseCommandState(string message, out PinType pinType, out bool state)
    {
        pinType = default;
        state = false;

        string[] values = message.Split(new[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 2 || !CommandPinTypes.TryGetValue(values[0].Trim(), out pinType))
        {
            return false;
        }

        return TryParseState(values[1], out state);
    }

    static bool TryParseState(string value, out bool state)
    {
        switch (value.Trim().ToUpperInvariant())
        {
            case "1":
            case "HIGH":
            case "TRUE":
            case "ON":
                state = true;
                return true;

            case "0":
            case "LOW":
            case "FALSE":
            case "OFF":
                state = false;
                return true;

            default:
                state = false;
                return false;
        }
    }
}
