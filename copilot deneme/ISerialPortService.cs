using Microsoft.UI.Dispatching;
using copilot_deneme.ViewModels;
using System;
using System.Threading.Tasks;

// ? YENÝ: Ayrýþtýrýlan data türlerini ekle
using copilot_deneme.TelemetryData;

namespace copilot_deneme
{
    /// <summary>
    /// ? YENÝ YAPIYLA GÜNCELLENEN INTERFACE
    /// Artýk ayrýþtýrýlan data türlerini kullanýr
    /// Duplicate event'ler ve eski referanslar temizlendi
    /// </summary>
    public interface ISerialPortService : IAsyncDisposable
    {
        #region Properties
        ChartViewModel? ViewModel { get; set; }
        DispatcherQueue? Dispatcher { get; set; }
        bool IsHyiTestMode { get; set; }
        
        // ? YENÝ: Auto HYI Generation kontrolü
        bool IsAutoHyiGenerationEnabled { get; set; }
        #endregion

        #region Events - YENÝ DATA TÜRLERÝYLE (ESKÝ DUPLICATE'LAR KALDIRILDI)
        event Action<string>? OnDataReceived;
        event Action<PayloadTelemetryData>? OnPayloadDataUpdated;     // ? YENÝ: copilot_deneme.TelemetryData.PayloadTelemetryData
        event Action<RocketTelemetryData>? OnRocketDataUpdated;       // ? YENÝ: copilot_deneme.TelemetryData.RocketTelemetryData
        event Action<HYITelemetryData>? OnHYIPacketReceived;          // ? YENÝ: copilot_deneme.TelemetryData.HYITelemetryData
        event Action<float, float, float>? OnRotationDataReceived;
        event Action<RocketTelemetryData, PayloadTelemetryData>? OnTelemetryDataUpdated; // ? YENÝ: Ayrýþtýrýlan türlerle
        event Action<string>? OnError;
        
        // ? YENÝ: HYIDenem event'i
        event Action<HYIDenemeData>? OnHYIDenemeDataUpdated;          // ? YENÝ: copilot_deneme.TelemetryData.HYIDenemeData
        #endregion

        #region Methods
        #region Serial Port Methods - SADECE PORT YÖNETÝMÝ
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
        #endregion

        #region Chart Update Methods - CHARTUPDATESERVICE'E DELEGATE EDÝLDÝ
        void UpdateChartsFromExternalData(float rocketAltitude, float payloadAltitude,
            float accelX, float accelY, float accelZ, float rocketSpeed, float payloadSpeed,
            float rocketTemp, float payloadTemp, float rocketPressure, float payloadPressure,
            float payloadHumidity, string source = "External");
        #endregion

        #region Output Port Methods - SADECE PORT YÖNETÝMÝ
        Task InitializeOutputPortAsync(string portName, int baudRate);
        Task WriteToOutputPortAsync(byte[] data);
        bool IsOutputPortOpen();
        Task CloseOutputPortAsync();
        #endregion

        #region HYI Test Methods - TESTMODEMANAGER'A DELEGATE EDÝLDÝ
        void StartHyiTestMode(int intervalMs = 2000);
        void StopHyiTestMode();
        Task<bool> SendManualHyiTestPacket();
        Task<bool> SendTestRocketPacket();
        Task<bool> SendTestPayloadPacket();
        Task<bool> SendCustomHyiPacket(
            byte teamId,
            byte packetCounter,
            float altitude,
            float rocketGpsAltitude,
            float rocketLatitude,
            float rocketLongitude,
            float payloadGpsAltitude,
            float payloadLatitude,
            float payloadLongitude,
            float stageGpsAltitude,
            float stageLatitude,
            float stageLongitude,
            float gyroscopeX,
            float gyroscopeY,
            float gyroscopeZ,
            float accelerationX,
            float accelerationY,
            float accelerationZ,
            float angle,
            byte status);
        Task<bool> SendZeroValueHyiPacket();
        #endregion

        #region HYI Generation Methods - TESTMODEMANAGER'A DELEGATE EDÝLDÝ
        void EnableAutoHyiGenerationOnly();
        void DisableAutoHyiGeneration();
        #endregion
        #endregion
    }

    // ===================================================================================================
    // ? ESKÝ INTERFACE KODLARI - YORUM SATIRINDAN SÝLÝNDÝ
    // ===================================================================================================
}