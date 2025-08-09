using copilot_deneme.TelemetryData;
using System;
using System.Threading.Tasks;

namespace copilot_deneme.Interfaces
{
    /// <summary>
    /// Telemetri paket iþlemcisi için interface
    /// Ham binary verileri telemetri data sýnýflarýna dönüþtürür
    /// </summary>
    public interface ITelemetryPacketProcessor
    {
        #region Events
        event Action<RocketTelemetryData>? OnRocketDataParsed;
        event Action<PayloadTelemetryData>? OnPayloadDataParsed;
        event Action<HYITelemetryData>? OnHYIDataParsed;
        event Action<HYIDenemeData>? OnHYIDenemeDataParsed;
        event Action<string>? OnParsingError;
        #endregion

        #region Methods
        /// <summary>
        /// Ham binary veriyi buffer'a ekler ve paket iþleme yapar
        /// </summary>
        void ProcessBinaryData(byte[] data);

        /// <summary>
        /// Buffer'ý temizler
        /// </summary>
        void ClearBuffer();

        /// <summary>
        /// Buffer durumu hakkýnda bilgi verir
        /// </summary>
        string GetBufferStatus();
        #endregion
    }

    /// <summary>
    /// Paket oluþturucu interface
    /// Telemetri data sýnýflarýný binary paketlere dönüþtürür
    /// </summary>
    public interface IPacketGenerator
    {
        #region HYI Packet Generation
        /// <summary>
        /// HYI telemetri verisini binary pakete dönüþtürür
        /// </summary>
        byte[] CreateHYIPacket(HYITelemetryData data);

        /// <summary>
        /// Özel parametrelerle HYI paketi oluþturur
        /// </summary>
        byte[] CreateCustomHYIPacket(
            byte teamId, byte packetCounter, float altitude,
            float rocketGpsAltitude, float rocketLatitude, float rocketLongitude,
            float payloadGpsAltitude, float payloadLatitude, float payloadLongitude,
            float stageGpsAltitude, float stageLatitude, float stageLongitude,
            float gyroscopeX, float gyroscopeY, float gyroscopeZ,
            float accelerationX, float accelerationY, float accelerationZ,
            float angle, byte status);

        /// <summary>
        /// Sýfýr deðerlerle HYI test paketi oluþturur
        /// </summary>
        byte[] CreateZeroHYIPacket();
        #endregion

        #region Test Packet Generation
        /// <summary>
        /// Test amaçlý roket paketi oluþturur
        /// </summary>
        byte[] CreateTestRocketPacket(RocketTelemetryData data);

        /// <summary>
        /// Test amaçlý payload paketi oluþturur
        /// </summary>
        byte[] CreateTestPayloadPacket(PayloadTelemetryData data);

        /// <summary>
        /// HYIDenem paketi oluþturur
        /// </summary>
        byte[] CreateHYIDenemePacket(HYIDenemeData data);
        #endregion

        #region Utility Methods
        /// <summary>
        /// CRC hesaplama (toplama yöntemi)
        /// </summary>
        byte CalculateChecksumAddition(byte[] data, int offset, int length);

        /// <summary>
        /// Basit CRC hesaplama (XOR yöntemi)
        /// </summary>
        byte CalculateSimpleCRC(byte[] data, int offset, int length);
        #endregion
    }

    /// <summary>
    /// Test modu yöneticisi interface
    /// HYI test sistemini ve otomatik paket üretimini yönetir
    /// </summary>
    public interface ITestModeManager
    {
        #region Properties
        bool IsHyiTestMode { get; }
        bool IsAutoHyiGenerationEnabled { get; set; }
        #endregion

        #region Events
        event Action<HYITelemetryData>? OnTestHYIDataGenerated;
        event Action<string>? OnTestModeStatusChanged;
        #endregion

        #region Test Mode Methods
        /// <summary>
        /// HYI test modunu baþlatýr (random veri üretimi)
        /// </summary>
        void StartHyiTestMode(int intervalMs = 2000);

        /// <summary>
        /// HYI test modunu durdurur
        /// </summary>
        void StopHyiTestMode();

        /// <summary>
        /// Arduino verilerinden HYI üretimini aktif eder
        /// </summary>
        void EnableAutoHyiGenerationOnly();

        /// <summary>
        /// Arduino verilerinden HYI üretimini devre dýþý býrakýr
        /// </summary>
        void DisableAutoHyiGeneration();
        #endregion

        #region HYI Generation from Rocket Data
        /// <summary>
        /// Roket telemetri verilerinden HYI paketi üretir
        /// </summary>
        HYITelemetryData GenerateHYIFromRocketData(RocketTelemetryData rocketData);

        /// <summary>
        /// Manuel test paketi üretir
        /// </summary>
        HYITelemetryData GenerateManualTestHYI();
        #endregion
    }

    /// <summary>
    /// Chart güncelleme servisi interface
    /// ViewModel ile iletiþim kurar ve chart'larý günceller
    /// </summary>
    public interface IChartUpdateService
    {
        #region Properties
        ViewModels.ChartViewModel? ViewModel { get; set; }
        Microsoft.UI.Dispatching.DispatcherQueue? Dispatcher { get; set; }
        #endregion

        #region Chart Update Methods
        /// <summary>
        /// Roket verilerini chart'lara ekler
        /// </summary>
        void UpdateChartsFromRocket(RocketTelemetryData rocketData);

        /// <summary>
        /// Payload verilerini chart'lara ekler
        /// </summary>
        void UpdateChartsFromPayload(PayloadTelemetryData payloadData);

        /// <summary>
        /// HYIDenem verilerini chart'lara ekler
        /// </summary>
        void UpdateChartsFromHYIDenem(HYIDenemeData hyiDenemeData);

        /// <summary>
        /// Dýþ kaynak verilerini chart'lara ekler
        /// </summary>
        void UpdateChartsFromExternalData(
            float rocketAltitude, float payloadAltitude,
            float accelX, float accelY, float accelZ, 
            float rocketSpeed, float payloadSpeed,
            float rocketTemp, float payloadTemp, 
            float rocketPressure, float payloadPressure,
            float payloadHumidity, string source = "External");


        /// <summary>
        /// Chart durumunu günceller
        /// </summary>
        void UpdateStatus(string status);
        #endregion

        #region Missing Methods
        /// <summary>
        /// Chart güncelleme servisi interface'e eksik metotlarý ekle
        /// </summary>
        void GenerateHYIDenemeFromRocketAndUpdateCharts(RocketTelemetryData rocketData);
        void UpdateChartsFromBothSources(RocketTelemetryData? rocketData, PayloadTelemetryData? payloadData);
        #endregion
    }
}