using Microsoft.UI.Dispatching;
using copilot_deneme.ViewModels;
using System;
using System.Threading.Tasks;

namespace copilot_deneme
{
    public interface ISerialPortService : IAsyncDisposable
    {
        #region Properties
        ChartViewModel? ViewModel { get; set; }
        DispatcherQueue? Dispatcher { get; set; }
        #endregion

        #region Events
        event Action<string>? OnDataReceived;
        event Action<SerialPortService.PayloadTelemetryData>? OnPayloadDataUpdated;
        event Action<SerialPortService.RocketTelemetryData>? OnRocketDataUpdated;
        event Action<SerialPortService.HYITelemetryData>? OnHYIPacketReceived;
        event Action<float, float, float>? OnRotationDataReceived;
        event Action<SerialPortService.RocketTelemetryData, SerialPortService.PayloadTelemetryData>? OnTelemetryDataUpdated;
        event Action<string>? OnError;
        #endregion

        #region Methods
        Task InitializeAsync(string portName, int baudRate);
        void Initialize(string portName, int baudRate);
        Task StartReadingAsync();
        void StartReading();
        Task StopReadingAsync();
        void StopReading();
        Task WriteAsync(byte[] data);
        void Write(byte[] data);
        bool IsPortOpen();
        string GetPortInfo();
        void UpdateChartsFromExternalData(float rocketAltitude, float payloadAltitude,
            float accelX, float accelY, float accelZ, float rocketSpeed, float payloadSpeed,
            float rocketTemp, float payloadTemp, float rocketPressure, float payloadPressure,
            float payloadHumidity, string source = "External",
            float rocketAccelX = 0, float rocketAccelY = 0);
        #endregion
    }
}