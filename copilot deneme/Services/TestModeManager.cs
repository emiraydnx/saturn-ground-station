using copilot_deneme.TelemetryData;
using copilot_deneme.Interfaces;
using System;
using System.Threading;
using System.Diagnostics;
using static copilot_deneme.TelemetryData.TelemetryConstants;

namespace copilot_deneme.Services
{
    /// <summary>
    /// Test modu yöneticisi - HYI test sistemini ve otomatik paket üretimini yönetir
    /// SerialPortService'den ayrýlan test sistemi sorumluluðu
    /// </summary>
    public class TestModeManager : ITestModeManager
    {
        private readonly Random _random = new Random();
        private Timer? _hyiTestTimer;
        private byte _testHyiPacketCounter = 0;

        #region Properties
        public bool IsHyiTestMode { get; private set; } = false;
        public bool IsAutoHyiGenerationEnabled { get; set; } = false;
        #endregion

        #region Events
        public event Action<HYITelemetryData>? OnTestHYIDataGenerated;
        public event Action<string>? OnTestModeStatusChanged;
        #endregion

        #region Test Mode Methods
        /// <summary>
        /// HYI test modunu baþlatýr (random veri üretimi)
        /// </summary>
        public void StartHyiTestMode(int intervalMs = 2000)
        {
            IsHyiTestMode = true;
            IsAutoHyiGenerationEnabled = true;
            _testHyiPacketCounter = 0;

            _hyiTestTimer = new Timer(GenerateRandomHyiData, null, 1000, intervalMs);
            
            OnTestModeStatusChanged?.Invoke($"?? HYI TEST MODU BAÞLATILDI! {intervalMs}ms aralýklarla random veri + Arduino verilerinden otomatik HYI üretimi aktif!");

            Debug.WriteLine($"???? HYI Test Modu baþlatýldý:");
            Debug.WriteLine($"   - IsHyiTestMode: {IsHyiTestMode}");
            Debug.WriteLine($"   - IsAutoHyiGenerationEnabled: {IsAutoHyiGenerationEnabled}");
            Debug.WriteLine($"   - Interval: {intervalMs}ms");
        }

        /// <summary>
        /// HYI test modunu durdurur
        /// </summary>
        public void StopHyiTestMode()
        {
            IsHyiTestMode = false;
            IsAutoHyiGenerationEnabled = false;
            _hyiTestTimer?.Dispose();
            _hyiTestTimer = null;

            OnTestModeStatusChanged?.Invoke("?? HYI TEST MODU DURDURULDU! Arduino verilerinden HYI üretimi de durduruldu.");
        }

        /// <summary>
        /// Arduino verilerinden HYI üretimini aktif eder
        /// </summary>
        public void EnableAutoHyiGenerationOnly()
        {
            IsAutoHyiGenerationEnabled = true;
            OnTestModeStatusChanged?.Invoke("?????? Arduino verilerinden HYI üretimi AKTÝF EDÝLDÝ!");

            Debug.WriteLine($"?? Sadece Arduino->HYI üretimi aktif edildi:");
            Debug.WriteLine($"   - IsAutoHyiGenerationEnabled: {IsAutoHyiGenerationEnabled}");
            Debug.WriteLine($"   - IsHyiTestMode: {IsHyiTestMode}");
        }

        /// <summary>
        /// Arduino verilerinden HYI üretimini devre dýþý býrakýr
        /// </summary>
        public void DisableAutoHyiGeneration()
        {
            IsAutoHyiGenerationEnabled = false;
            OnTestModeStatusChanged?.Invoke("?? Arduino verilerinden HYI üretimi DURDURULDU!");

            Debug.WriteLine($"?? Arduino->HYI üretimi durduruldu:");
            Debug.WriteLine($"   - IsAutoHyiGenerationEnabled: {IsAutoHyiGenerationEnabled}");
        }
        #endregion

        #region HYI Generation from Rocket Data
        /// <summary>
        /// Roket telemetri verilerinden HYI paketi üretir
        /// </summary>
        public HYITelemetryData GenerateHYIFromRocketData(RocketTelemetryData rocketData)
        {
            try
            {
                Debug.WriteLine($"?????? GenerateHYIFromRocketData çaðrýldý!");
                Debug.WriteLine($"   - IsAutoHyiGenerationEnabled: {IsAutoHyiGenerationEnabled}");
                Debug.WriteLine($"   - Roket verisi: Ýrtifa={rocketData.RocketAltitude:F2}m, TeamID={rocketData.TeamID}, Counter={rocketData.PacketCounter}");

                var hyiData = new HYITelemetryData
                {
                    TeamId = rocketData.TeamID > 0 ? rocketData.TeamID : (byte)123, // TeamID varsa kullan, yoksa default
                    PacketCounter = rocketData.PacketCounter,

                    // Roket verilerini direkt kullan
                    Altitude = rocketData.RocketAltitude,
                    RocketGpsAltitude = rocketData.RocketGpsAltitude,
                    RocketLatitude = rocketData.RocketLatitude,
                    RocketLongitude = rocketData.RocketLongitude,

                    // Payload GPS koordinatlarý roket ile ayný
                    PayloadGpsAltitude = rocketData.RocketGpsAltitude,
                    PayloadLatitude = rocketData.RocketLatitude,
                    PayloadLongitude = rocketData.RocketLongitude,

                    // Stage GPS koordinatlarý roket ile benzer (biraz farklý)
                    StageGpsAltitude = rocketData.RocketGpsAltitude - 10f, // 10m daha düþük
                    StageLatitude = rocketData.RocketLatitude - 0.001f,   // Biraz güneyde
                    StageLongitude = rocketData.RocketLongitude - 0.001f, // Biraz batýda

                    // Roket sensör verilerini direkt kullan
                    GyroscopeX = rocketData.GyroX,
                    GyroscopeY = rocketData.GyroY,
                    GyroscopeZ = rocketData.GyroZ,
                    AccelerationX = rocketData.AccelX,
                    AccelerationY = rocketData.AccelY,
                    AccelerationZ = rocketData.AccelZ,
                    Angle = rocketData.Angle,

                    Status = rocketData.status,
                    CRC = 0 // CRC otomatik hesaplanacak
                };

                Debug.WriteLine($"?? HYI paketi oluþturuldu: #{hyiData.PacketCounter}, Ýrtifa: {hyiData.Altitude:F2}m");
                return hyiData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"? GenerateHYIFromRocketData hatasý: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Manuel test paketi üretir
        /// </summary>
        public HYITelemetryData GenerateManualTestHYI()
        {
            return new HYITelemetryData
            {
                TeamId = 123,
                PacketCounter = _testHyiPacketCounter++,
                Altitude = 250.5f,
                RocketGpsAltitude = 255.2f,
                RocketLatitude = 39.925533f,
                RocketLongitude = 32.866287f,
                PayloadGpsAltitude = 240.1f,
                PayloadLatitude = 39.925000f,
                PayloadLongitude = 32.866000f,
                StageGpsAltitude = 235.8f,
                StageLatitude = 39.924500f,
                StageLongitude = 32.865500f,
                GyroscopeX = 45.2f,
                GyroscopeY = -12.8f,
                GyroscopeZ = 78.3f,
                AccelerationX = 2.1f,
                AccelerationY = -1.5f,
                AccelerationZ = 9.81f,
                Angle = 125.4f,
                Status = 3,
                CRC = 0
            };
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Random HYI verisi oluþtur ve event'i tetikle
        /// </summary>
        private void GenerateRandomHyiData(object? state)
        {
            try
            {
                var hyiData = new HYITelemetryData
                {
                    TeamId = 123, // Sabit takým ID
                    PacketCounter = _testHyiPacketCounter++,

                    // Random koordinatlar (Ankara çevresi)
                    Altitude = (float)(_random.NextDouble() * 1000), // 0-1000m
                    RocketGpsAltitude = (float)(_random.NextDouble() * 1000 + 50), // 50-1050m
                    RocketLatitude = 39.925533f + (float)(_random.NextDouble() * 0.01 - 0.005), // Ankara ±0.005°
                    RocketLongitude = 32.866287f + (float)(_random.NextDouble() * 0.01 - 0.005),

                    PayloadGpsAltitude = (float)(_random.NextDouble() * 500 + 10), // 10-510m
                    PayloadLatitude = 39.925533f + (float)(_random.NextDouble() * 0.008 - 0.004),
                    PayloadLongitude = 32.866287f + (float)(_random.NextDouble() * 0.008 - 0.004),

                    StageGpsAltitude = (float)(_random.NextDouble() * 200 + 5), // 5-205m
                    StageLatitude = 39.925533f + (float)(_random.NextDouble() * 0.006 - 0.003),
                    StageLongitude = 32.866287f + (float)(_random.NextDouble() * 0.006 - 0.003),

                    // Random sensör verileri
                    GyroscopeX = (float)(_random.NextDouble() * 360 - 180), // ±180°/s
                    GyroscopeY = (float)(_random.NextDouble() * 360 - 180),
                    GyroscopeZ = (float)(_random.NextDouble() * 360 - 180),

                    AccelerationX = (float)(_random.NextDouble() * 40 - 20), // ±20m/s²
                    AccelerationY = (float)(_random.NextDouble() * 40 - 20),
                    AccelerationZ = 9.81f + (float)(_random.NextDouble() * 20 - 10), // ~9.81 ±10

                    Angle = (float)(_random.NextDouble() * 360), // 0-360°
                    Status = (byte)(_random.Next(1, 6)), // Durum 1-5 arasý
                    CRC = 0 // CRC sonradan hesaplanacak
                };

                // Event'i tetikle
                OnTestHYIDataGenerated?.Invoke(hyiData);

                Debug.WriteLine($"?? TEST HYI #{hyiData.PacketCounter} - Alt: {hyiData.Altitude:F1}m, Pos: {hyiData.RocketLatitude:F6},{hyiData.RocketLongitude:F6}");
            }
            catch (Exception ex)
            {
                OnTestModeStatusChanged?.Invoke($"HYI test hatasý: {ex.Message}");
                Debug.WriteLine($"HYI test verisi oluþturma hatasý: {ex.Message}");
            }
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            _hyiTestTimer?.Dispose();
            _hyiTestTimer = null;
        }
        #endregion
    }
}