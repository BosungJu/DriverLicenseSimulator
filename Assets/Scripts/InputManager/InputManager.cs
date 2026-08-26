using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

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

    const int ReadBufferSize = 1024;
    const int ReadTimeoutMilliseconds = 100;
    const int ThreadJoinTimeoutMilliseconds = 500;

    [Header("Serial Port")]
    [SerializeField] string portName = "COM3";
    [SerializeField] int baudRate = 115200;
    [SerializeField] bool autoConnect = true;
    [SerializeField] bool dtrEnable = true;

    readonly ConcurrentQueue<string> receivedInputs = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> serialErrors = new ConcurrentQueue<string>();

    SerialPort serialPort;
    Thread readThread;
    volatile bool isReading;
    volatile bool disconnectRequested;

    public bool IsConnected => serialPort != null && serialPort.IsOpen;

    void OnEnable()
    {
        if (autoConnect)
        {
            Connect();
        }
    }

    void Update()
    {
        while (receivedInputs.TryDequeue(out string serialInput))
        {
            Debug.Log(serialInput, this);
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

    void ReadSerialPort(SerialPort openedPort)
    {
        char[] readBuffer = new char[ReadBufferSize];

        while (isReading && openedPort.IsOpen)
        {
            try
            {
                int readCharacterCount = openedPort.Read(readBuffer, 0, readBuffer.Length);
                if (readCharacterCount > 0)
                {
                    receivedInputs.Enqueue(new string(readBuffer, 0, readCharacterCount));
                }
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

}
