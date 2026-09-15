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
        Debug.Log("[InputManager] 스크립트 실행됨");

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
                ReadTimeout = ReadTimeoutMilliseconds,
                NewLine = "\n"
            };
            openedPort.Open();

            Debug.Log(
                $"[InputManager] {portName} 연결 성공",
                this
            );
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
        while (isReading && openedPort.IsOpen)
        {
            try
            {
                string serialInput = openedPort.ReadLine().Trim();

                if (!string.IsNullOrEmpty(serialInput))
                {
                    receivedInputs.Enqueue(serialInput);
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
