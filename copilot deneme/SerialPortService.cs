using copilot_deneme.ViewModels;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace copilot_deneme
{
    public class SerialPortService : ISerialPortService
    {
        private readonly ILogger<SerialPortService>? _logger;
        private SerialPort? _inputPort;
        private SerialPort? _outputPort;
        private readonly ConcurrentQueue<byte[]> _dataQueue = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _processingTask;
        
        // Thread-safe buffer
        private readonly object _bufferLock = new object();
        private readonly List<byte> _binaryBuffer = new List<byte>();

        // Configuration constants
        private const int HYI_PACKET_SIZE = 78;
        private static readonly byte[] HYI_HEADER = { 0xFF, 0xFF, 0x54, 0x52 };
        private const int ROCKET_PACKET_SIZE = 64;
        private static readonly byte[] ROCKET_HEADER = { 0xAB, 0xBC, 0x12, 0x13 };
        private const int PAYLOAD_PACKET_SIZE = 34;
        private static readonly byte[] PAYLOAD_HEADER = { 0xCD, 0xDF, 0x14, 0x15 };
        
        // ✨ YENİ: HYIDenem paket sabitleri
        private const int HYIDENEM_PACKET_SIZE = 98; // Roket (64) + Payload (34) = 98 byte
        private static readonly byte[] HYIDENEM_HEADER = { 0xDE, 0xAD, 0xBE, 0xEF }; // Özel header
        
        private const int MAX_BUFFER_SIZE = 4096; // Buffer overflow koruması

        // TEST HYI VERİSİ İÇİN RANDOM GENERATORİ
        private readonly Random _random = new Random();
        private Timer? _hyiTestTimer;
        private byte _testHyiPacketCounter = 0;
        public bool IsHyiTestMode { get; set; } = false; // TEST MODU AÇMA/KAPAMA
        
        // ✨ YENİ: Gerçek Arduino verilerinden HYI paketi oluşturma için flag
        public bool IsAutoHyiGenerationEnabled { get; set; } = false; // Arduino verilerinden otomatik HYI üretimi
        
        // ✨ YENİ: HYIDenem test verileri için sabit değerler (±0.05 değişim)
        private const float BASE_PAYLOAD_ALTITUDE = 320.5f;
        private const float BASE_PAYLOAD_SPEED = 45.8f;
        private const float BASE_PAYLOAD_TEMPERATURE = 22.3f;
        private const float BASE_PAYLOAD_PRESSURE = 1015.2f;
        private const float BASE_PAYLOAD_HUMIDITY = 65.7f;
        private const float PAYLOAD_VARIATION = 0.05f; // ±0.05 değişim

        public SerialPortService(ILogger<SerialPortService>? logger = null)
        {
            _logger = logger;
            
            // ✅ BAŞLANGIÇ DURUMU DEBUG BİLGİSİ
            Debug.WriteLine($"🔧 SerialPortService oluşturuldu:");
            Debug.WriteLine($"   - IsHyiTestMode: {IsHyiTestMode}");
            Debug.WriteLine($"   - IsAutoHyiGenerationEnabled: {IsAutoHyiGenerationEnabled}");
        }

        #region Properties
        public ChartViewModel? ViewModel { get; set; }
        public DispatcherQueue? Dispatcher { get; set; }
        #endregion

        #region Events
        public event Action<string>? OnDataReceived;
        public event Action<PayloadTelemetryData>? OnPayloadDataUpdated;
        public event Action<RocketTelemetryData>? OnRocketDataUpdated;
        public event Action<HYITelemetryData>? OnHYIPacketReceived;
        public event Action<float, float, float>? OnRotationDataReceived;
        public event Action<RocketTelemetryData, PayloadTelemetryData>? OnTelemetryDataUpdated;
        public event Action<string>? OnError; // Yeni hata event'i
        
        // ✨ YENİ: HYIDenem event'i
        public event Action<HYIDenemeData>? OnHYIDenemeDataUpdated;
        #endregion

        #region Data Classes
        public class RocketTelemetryData
        {
            public float RocketAltitude { get; set; }
            public float RocketGpsAltitude { get; set; }
            public float RocketLatitude { get; set; }
            public float RocketLongitude { get; set; }
            public float GyroX { get; set; }
            public float GyroY { get; set; }
            public float GyroZ { get; set; }
            public float AccelX { get; set; }
            public float AccelY { get; set; }
            public float AccelZ { get; set; }
            public float Angle { get; set; }
            public float RocketSpeed { get; set; }
            public float RocketTemperature { get; set; }  
            public float RocketPressure { get; set; }
            public byte CRC { get; set; }
            public byte TeamID { get; set; }
            public byte status { get; set; }
            public byte PacketCounter { get; set; }
        }
            
        public class PayloadTelemetryData
        {
            public float PayloadGpsAltitude { get; set; }
            public float PayloadAltitude { get; set; }
            public float PayloadLatitude { get; set; }
            public float PayloadLongitude { get; set; }
            public float PayloadSpeed { get; set; }
            public float PayloadTemperature { get; set; }
            public float PayloadPressure { get; set; }
            public float PayloadHumidity { get; set; }
            public byte CRC { get; set; }
            public byte PacketCounter { get; set; }
        }

        public class HYITelemetryData
        {
            public byte TeamId { get; set; }
            public byte PacketCounter { get; set; }
            public float Altitude { get; set; }
            public float RocketGpsAltitude { get; set; }
            public float RocketLatitude { get; set; }
            public float RocketLongitude { get; set; }
            public float PayloadGpsAltitude { get; set; }
            public float PayloadLatitude { get; set; }
            public float PayloadLongitude { get; set; }
            public float StageGpsAltitude { get; set; }
            public float StageLatitude { get; set; }
            public float StageLongitude { get; set; }
            public float GyroscopeX { get; set; }
            public float GyroscopeY { get; set; }
            public float GyroscopeZ { get; set; }
            public float AccelerationX { get; set; }
            public float AccelerationY { get; set; }
            public float AccelerationZ { get; set; }
            public float Angle { get; set; }
            public byte Status { get; set; }
            public byte CRC { get; set; }
        }

        /// <summary>
        /// ✨ YENİ: HYIDenem paketi - Roket telemetrilerinin HEPSİ + Payload verileri
        /// Payload GPS koordinatları roketle aynı, diğerleri sabit ±0.05 değişim
        /// </summary>
        public class HYIDenemeData
        {
            // Temel paket bilgileri
            public byte TeamId { get; set; }
            public byte PacketCounter { get; set; }
            
            // TÜM ROKET TELEMETRİLERİ
            public float RocketAltitude { get; set; }
            public float RocketGpsAltitude { get; set; }
            public float RocketLatitude { get; set; }
            public float RocketLongitude { get; set; }
            public float GyroX { get; set; }
            public float GyroY { get; set; }
            public float GyroZ { get; set; }
            public float AccelX { get; set; }
            public float AccelY { get; set; }
            public float AccelZ { get; set; }
            public float Angle { get; set; }
            public float RocketSpeed { get; set; }
            public float RocketTemperature { get; set; }  
            public float RocketPressure { get; set; }
            public byte RocketStatus { get; set; }
            
            // PAYLOAD VERİLERİ (GPS: roketle aynı, diğerleri: sabit ±0.05)
            public float PayloadGpsAltitude { get; set; }     // Roket GPS'inden türetilecek
            public float PayloadAltitude { get; set; }        // Sabit ±0.05
            public float PayloadLatitude { get; set; }        // Roket enlem ile aynı
            public float PayloadLongitude { get; set; }       // Roket boylam ile aynı
            public float PayloadSpeed { get; set; }           // Sabit ±0.05
            public float PayloadTemperature { get; set; }     // Sabit ±0.05
            public float PayloadPressure { get; set; }        // Sabit ±0.05
            public float PayloadHumidity { get; set; }        // Sabit ±0.05
            
            // Paket son bilgileri
            public byte CRC { get; set; }
        }
        #endregion

        // Helper methods
        private RocketTelemetryData? _lastRocketData;
        private PayloadTelemetryData? _lastPayloadData;

        #region HYI TEST MOD METODLARİ
        
        /// <summary>
        /// HYI test modunu başlat - Random verilerle HYI paketleri oluşturur VE gerçek Arduino verilerinden de HYI üretmeyi aktif eder
        /// </summary>
        public void StartHyiTestMode(int intervalMs = 2000)
        {
            IsHyiTestMode = true;
            IsAutoHyiGenerationEnabled = true; // ✨ YENİ: Arduino verilerinden otomatik HYI üretimi aktif!
            _testHyiPacketCounter = 0;
            
            _hyiTestTimer = new Timer(GenerateRandomHyiData, null, 1000, intervalMs);
            OnDataReceived?.Invoke($"🧪 HYI TEST MODU BAŞLATILDI! {intervalMs}ms aralıklarla random veri + Arduino verilerinden otomatik HYI üretimi aktif!");
            
            // ✅ DEBUG BİLGİSİ EKLE
            Debug.WriteLine($"🚀🔧 HYI Test Modu başlatıldı:");
            Debug.WriteLine($"   - IsHyiTestMode: {IsHyiTestMode}");
            Debug.WriteLine($"   - IsAutoHyiGenerationEnabled: {IsAutoHyiGenerationEnabled}");
            Debug.WriteLine($"   - Interval: {intervalMs}ms");
            Debug.WriteLine($"   - Output Port Open: {IsOutputPortOpen()}");
            
            _logger?.LogInformation("HYI Test Modu başlatıldı - Interval: {IntervalMs}ms, Auto Generation: true", intervalMs);
        }

        /// <summary>
        /// HYI test modunu durdur
        /// </summary>
        public void StopHyiTestMode()
        {
            IsHyiTestMode = false;
            IsAutoHyiGenerationEnabled = false; // ✨ YENİ: Arduino verilerinden otomatik HYI üretimi de durur
            _hyiTestTimer?.Dispose();
            _hyiTestTimer = null;
            
            OnDataReceived?.Invoke("🛑 HYI TEST MODU DURDURULDU! Arduino verilerinden HYI üretimi de durduruldu.");
            _logger?.LogInformation("HYI Test Modu durduruldu, Auto Generation: false");
        }

        /// <summary>
        /// Random HYI verisi oluştur ve event'i tetikle
        /// </summary>
        private void GenerateRandomHyiData(object? state)
        {
            try
            {
                var hyiData = new HYITelemetryData
                {
                    TeamId = 123, // Sabit takım ID
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
                    Status = (byte)(_random.Next(1, 6)), // Durum 1-5 arası
                    CRC = 0 // CRC sonradan hesaplanacak
                };

                // Event'i tetikle (UI için)
                Dispatcher?.TryEnqueue(() => OnHYIPacketReceived?.Invoke(hyiData));
                
                // HYI verisini binary paket haline getir
                byte[] hyiPacket = ConvertHyiDataToPacket(hyiData);
                
                // Output port'a gönder (eğer açıksa)
                if (IsOutputPortOpen() && hyiPacket != null)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await WriteToOutputPortAsync(hyiPacket);
                            OnDataReceived?.Invoke($"📤 HYI TEST paketi gönderildi: {hyiPacket.Length} byte - #{hyiData.PacketCounter}");
                            _logger?.LogDebug("HYI test paketi gönderildi: #{PacketCounter}, {Size} byte", hyiData.PacketCounter, hyiPacket.Length);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "HYI test paket gönderme hatası");
                            OnError?.Invoke($"HYI test paket gönderme hatası: {ex.Message}");
                        }
                    });
                }
                else if (!IsOutputPortOpen())
                {
                    // Output port açık değilse sadece UI event'ini tetikle
                    OnDataReceived?.Invoke($"🧪 HYI TEST #{hyiData.PacketCounter} oluşturuldu (Output port kapalı) - Alt: {hyiData.Altitude:F1}m");
                }
                
                // Debug bilgisi
                OnDataReceived?.Invoke($"🧪 TEST HYI #{hyiData.PacketCounter} - Alt: {hyiData.Altitude:F1}m, Pos: {hyiData.RocketLatitude:F6},{hyiData.RocketLongitude:F6}");
                
                _logger?.LogDebug("Test HYI verisi oluşturuldu: #{PacketCounter}, Altitude: {Altitude:F2}m", 
                    hyiData.PacketCounter, hyiData.Altitude);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "HYI test verisi oluşturma hatası");
                OnError?.Invoke($"HYI test hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// ✨ YENİ: Arduino'dan gelen gerçek roket telemetri verilerinden HYI paketi oluşturup gönder
        /// HYI test sistemi aktifken çalışır
        /// </summary>
        private async void GenerateAndSendHyiFromRocketData(RocketTelemetryData rocketData)
        {
            // ✅ DETAYLI DEBUG BİLGİSİ
            Debug.WriteLine($"🚀➡️📡 GenerateAndSendHyiFromRocketData çağrıldı!");
            Debug.WriteLine($"   - IsAutoHyiGenerationEnabled: {IsAutoHyiGenerationEnabled}");
            Debug.WriteLine($"   - IsOutputPortOpen(): {IsOutputPortOpen()}");
            Debug.WriteLine($"   - Roket verisi: İrtifa={rocketData.RocketAltitude:F2}m, TeamID={rocketData.TeamID}, Counter={rocketData.PacketCounter}");
            
            if (!IsAutoHyiGenerationEnabled)
            {
                Debug.WriteLine("❌ HYI üretimi aktif değil - çıkılıyor");
                OnDataReceived?.Invoke("⚠️ Arduino verilerinden HYI üretimi aktif değil! HYI test modunu başlatın.");
                return; // HYI üretimi aktif değil
            }

            try
            {
                // Payload verileri için küçük random değişimler (±0.05)
                float payloadAltitudeVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadSpeedVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadTempVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadPressureVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadHumidityVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);

                // Arduino verilerinden HYI paketi oluştur
                var hyiData = new HYITelemetryData
                {
                    TeamId = rocketData.TeamID > 0 ? rocketData.TeamID : (byte)123, // TeamID varsa kullan, yoksa default
                    PacketCounter = rocketData.PacketCounter,
                    
                    // Roket verilerini direkt kullan
                    Altitude = rocketData.RocketAltitude,
                    RocketGpsAltitude = rocketData.RocketGpsAltitude,
                    RocketLatitude = rocketData.RocketLatitude,
                    RocketLongitude = rocketData.RocketLongitude,
                    
                    // Payload GPS koordinatları roket ile aynı
                    PayloadGpsAltitude = rocketData.RocketGpsAltitude,
                    PayloadLatitude = rocketData.RocketLatitude,
                    PayloadLongitude = rocketData.RocketLongitude,
                    
                    // Stage GPS koordinatları roket ile benzer (biraz farklı)
                    StageGpsAltitude = rocketData.RocketGpsAltitude - 10f, // 10m daha düşük
                    StageLatitude = rocketData.RocketLatitude - 0.001f,   // Biraz güneyde
                    StageLongitude = rocketData.RocketLongitude - 0.001f, // Biraz batıda
                    
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

                Debug.WriteLine($"🔧 HYI paketi oluşturuldu: #{hyiData.PacketCounter}, İrtifa: {hyiData.Altitude:F2}m");

                // HYI verisini binary paket haline getir
                byte[] hyiPacket = ConvertHyiDataToPacket(hyiData);
                
                // Output port'a gönder (eğer açıksa)
                if (IsOutputPortOpen())
                {
                    await WriteToOutputPortAsync(hyiPacket);
                    OnDataReceived?.Invoke($"🚀➡️📡 ARDUINO VERİSİNDEN HYI PAKETİ GÖNDERİLDİ! #{hyiData.PacketCounter} - İrtifa: {hyiData.Altitude:F1}m, {hyiPacket.Length} byte");
                    Debug.WriteLine($"✅ HYI paketi output port'a gönderildi: {hyiPacket.Length} byte");
                }
                else
                {
                    // Output port kapalı olsa bile HYI event'ini tetikle
                    OnDataReceived?.Invoke($"🚀➡️📡 ARDUINO VERİSİNDEN HYI ÜRETİLDİ (Output port kapalı)! #{hyiData.PacketCounter} - İrtifa: {hyiData.Altitude:F1}m");
                    Debug.WriteLine($"⚠️ Output port kapalı, sadece HYI verisi oluşturuldu");
                }
                
                // Event'i tetikle (UI için) - OUTPUT PORT DURUMUNDAN BAĞIMSIZ!
                Dispatcher?.TryEnqueue(() => OnHYIPacketReceived?.Invoke(hyiData));
                
                _logger?.LogInformation("Arduino verilerinden HYI paketi oluşturuldu: #{PacketCounter}, Altitude: {Altitude:F2}m, OutputPortOpen: {OutputPortOpen}", 
                    hyiData.PacketCounter, hyiData.Altitude, IsOutputPortOpen());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Arduino verilerinden HYI paketi oluşturma hatası");
                OnError?.Invoke($"Arduino->HYI dönüştürme hatası: {ex.Message}");
                Debug.WriteLine($"❌ GenerateAndSendHyiFromRocketData hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// HYI modülüne manuel HYI paketi gönder
        /// </summary>
        public async Task<bool> SendManualHyiTestPacket()
        {
            try
            {
                var testData = new HYITelemetryData
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

                byte[] packet = ConvertHyiDataToPacket(testData);
                
                if (IsOutputPortOpen())
                {
                    await WriteToOutputPortAsync(packet);
                    OnDataReceived?.Invoke($"📤 MANUEL HYI paketi gönderildi: #{testData.PacketCounter} - {packet.Length} byte");
                    _logger?.LogInformation("Manuel HYI test paketi gönderildi: #{PacketCounter}", testData.PacketCounter);
                    return true;
                }
                else
                {
                    OnDataReceived?.Invoke("❌ Output port açık değil - HYI paketi gönderilemedi");
                    OnError?.Invoke("Output port açık değil");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Manuel HYI test paketi gönderme hatası");
                OnError?.Invoke($"Manuel HYI test hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Hazır Roket test verisi oluştur ve gönder
        /// </summary>
        public async Task<bool> SendTestRocketPacket()
        {
            try
            {
                var testData = new RocketTelemetryData
                {
                    PacketCounter = (byte)(_testHyiPacketCounter % 256),
                    RocketAltitude = 450.8f,
                    RocketGpsAltitude = 452.3f,
                    RocketLatitude = 39.925533f,
                    RocketLongitude = 32.866287f,
                    GyroX = 15.2f,
                    GyroY = -8.7f,
                    GyroZ = 22.1f,
                    AccelX = 3.2f,
                    AccelY = -2.1f,
                    AccelZ = 12.5f,
                    Angle = 87.3f,
                    RocketSpeed = 125.6f,
                    RocketTemperature = 23.4f,
                    RocketPressure = 1013.25f,
                    status = 2,
                    CRC = 0,
                    TeamID = 123
                };

                byte[] packet = ConvertRocketDataToPacket(testData);
                
                if (_inputPort?.IsOpen == true)
                {
                    await WriteAsync(packet);
                    OnDataReceived?.Invoke($"📤 TEST ROKET paketi gönderildi: #{testData.PacketCounter} - {packet.Length} byte");
                    _logger?.LogInformation("Test roket paketi gönderildi: #{PacketCounter}", testData.PacketCounter);
                    return true;
                }
                else
                {
                    OnDataReceived?.Invoke("❌ Input port açık değil - Roket paketi gönderilemedi");
                    OnError?.Invoke("Input port açık değil");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Test roket paketi gönderme hatası");
                OnError?.Invoke($"Test payload hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Hazır Payload test verisi oluştur ve gönder
        /// </summary>
        public async Task<bool> SendTestPayloadPacket()
        {
            try
            {
                var testData = new PayloadTelemetryData
                {
                    PacketCounter = (byte)(_testHyiPacketCounter % 256),
                    PayloadAltitude = 320.5f,
                    PayloadGpsAltitude = 322.1f,
                    PayloadLatitude = 39.925000f,
                    PayloadLongitude = 32.866000f,
                    PayloadSpeed = 85.3f,
                    PayloadTemperature = 18.7f,
                    PayloadPressure = 1015.8f,
                    PayloadHumidity = 65.2f,
                    CRC = 0
                };

                byte[] packet = ConvertPayloadDataToPacket(testData);
                
                if (_inputPort?.IsOpen == true)
                {
                    await WriteAsync(packet);
                    OnDataReceived?.Invoke($"📤 TEST PAYLOAD paketi gönderildi: #{testData.PacketCounter} - {packet.Length} byte");
                    _logger?.LogInformation("Test payload paketi gönderildi: #{PacketCounter}", testData.PacketCounter);
                    return true;
                }
                else
                {
                    OnDataReceived?.Invoke("❌ Input port açık değil - Payload paketi gönderilemedi");
                    OnError?.Invoke("Input port açık değil");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Test payload paketi gönderme hatası");
                OnError?.Invoke($"Test payload hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// HYI telemetri verisini 78 byte binary pakete dönüştür - VERDİĞİNİZ SIRAYA GÖRE
        /// </summary>
        private static byte[] ConvertHyiDataToPacket(HYITelemetryData data)
        {
            try
            {
                byte[] packet = new byte[HYI_PACKET_SIZE];
                int offset = 0;

                // Byte 1-4: Header (0xFF, 0xFF, 0x54, 0x52)
                packet[offset++] = 0xFF;
                packet[offset++] = 0xFF;
                packet[offset++] = 0x54;
                packet[offset++] = 0x52;

                // Byte 5: TAKIM ID
                packet[offset++] = data.TeamId;

                // Byte 6: PAKET SAYAÇ
                packet[offset++] = data.PacketCounter;

                // Byte 7-10: İRTİFA (FLOAT32)
                BitConverter.GetBytes(data.Altitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 11-14: ROKET GPS İRTİFA (FLOAT32)
                BitConverter.GetBytes(data.RocketGpsAltitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 15-18: ROKET ENLEM (FLOAT32)
                BitConverter.GetBytes(data.RocketLatitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 19-22: ROKET BOYLAM (FLOAT32)
                BitConverter.GetBytes(data.RocketLongitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 23-26: GÖREV YÜKÜ GPS İRTİFA (FLOAT32)
                BitConverter.GetBytes(data.PayloadGpsAltitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 27-30: GÖREV YÜKÜ ENLEM (FLOAT32)
                BitConverter.GetBytes(data.PayloadLatitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 31-34: GÖREV YÜKÜ BOYLAM (FLOAT32)
                BitConverter.GetBytes(data.PayloadLongitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 35-38: KADEME GPS İRTİFA (FLOAT32)
                BitConverter.GetBytes(data.StageGpsAltitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 39-42: KADEME ENLEM (FLOAT32)
                BitConverter.GetBytes(data.StageLatitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 43-46: KADEME BOYLAM (FLOAT32)
                BitConverter.GetBytes(data.StageLongitude).CopyTo(packet, offset);
                offset += 4;

                // Byte 47-50: JİROSKOP X (FLOAT32)
                BitConverter.GetBytes(data.GyroscopeX).CopyTo(packet, offset);
                offset += 4;

                // Byte 51-54: JİROSKOP Y (FLOAT32)
                BitConverter.GetBytes(data.GyroscopeY).CopyTo(packet, offset);
                offset += 4;

                // Byte 55-58: JİROSKOP Z (FLOAT32)
                BitConverter.GetBytes(data.GyroscopeZ).CopyTo(packet, offset);
                offset += 4;

                // Byte 59-62: İVME X (FLOAT32)
                BitConverter.GetBytes(data.AccelerationX).CopyTo(packet, offset);
                offset += 4;

                // Byte 63-66: İVME Y (FLOAT32)
                BitConverter.GetBytes(data.AccelerationY).CopyTo(packet, offset);
                offset += 4;

                // Byte 67-70: İVME Z (FLOAT32)
                BitConverter.GetBytes(data.AccelerationZ).CopyTo(packet, offset);
                offset += 4;

                // Byte 71-74: AÇI (FLOAT32)
                BitConverter.GetBytes(data.Angle).CopyTo(packet, offset);
                offset += 4;

                // Byte 75: DURUM (UINT8)
                packet[offset++] = data.Status;

                // Byte 76: CRC (UINT8) - Header hariç tüm data için C kodunuza uygun TOPLAMA YÖNTEMİ
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, offset - 4);
                packet[offset++] = calculatedCRC;

                // Byte 77: 0x0D (CR)
                packet[offset++] = 0x0D;

                // Byte 78: 0x0A (LF)
                packet[offset++] = 0x0A;

                Debug.WriteLine($"HYI paketi oluşturuldu: {packet.Length} byte, offset: {offset}");
                Debug.WriteLine($"Header: {packet[0]:X2} {packet[1]:X2} {packet[2]:X2} {packet[3]:X2}");
                Debug.WriteLine($"Takım ID: {packet[4]}, Sayaç: {packet[5]}");
                Debug.WriteLine($"İrtifa: {BitConverter.ToSingle(packet, 6):F2}m");
                Debug.WriteLine($"CRC hesaplama: Byte 4-74 arası, Hesaplanan: 0x{calculatedCRC:X2}");
                Debug.WriteLine($"Son 3 byte: Status=0x{packet[74]:X2}, CRC=0x{packet[75]:X2}, CR=0x{packet[76]:X2}, LF=0x{packet[77]:X2}");
                
                // TAM PAKET HEX DUMP
                string fullHex = BitConverter.ToString(packet).Replace("-", " ");
                Debug.WriteLine($"TAM HYI PAKET: {fullHex}");
                return packet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HYI paket dönüştürme hatası: {ex.Message}");
                return new byte[HYI_PACKET_SIZE]; // Boş paket döndür
            }
        }

        /// <summary>
        /// Roket telemetri verisini 64 byte binary pakete dönüştür
        /// </summary>
        private static byte[] ConvertRocketDataToPacket(RocketTelemetryData data)
        {
            try
            {
                byte[] packet = new byte[ROCKET_PACKET_SIZE];
                int offset = 0;

                // Header (4 byte): 0xAB, 0xBC, 0x12, 0x13
                Array.Copy(ROCKET_HEADER, 0, packet, offset, ROCKET_HEADER.Length);
                offset += ROCKET_HEADER.Length;

                // Packet Counter (1 byte)
                packet[offset++] = data.PacketCounter;

                // Altitude values (floats - 4 bytes each)
                BitConverter.GetBytes(data.RocketAltitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.RocketGpsAltitude).CopyTo(packet, offset);
                offset += 4;

                // GPS coordinates (floats - 4 bytes each)
                BitConverter.GetBytes(data.RocketLatitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.RocketLongitude).CopyTo(packet, offset);
                offset += 4;

                // Gyroscope values (floats - 4 bytes each)
                BitConverter.GetBytes(data.GyroX).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.GyroY).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.GyroZ).CopyTo(packet, offset);
                offset += 4;

                // Acceleration values (floats - 4 bytes each)
                BitConverter.GetBytes(data.AccelX).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.AccelY).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.AccelZ).CopyTo(packet, offset);
                offset += 4;

                // Angle (float - 4 bytes)
                BitConverter.GetBytes(data.Angle).CopyTo(packet, offset);
                offset += 4;

                // Temperature and Pressure (floats - 4 bytes each)
                BitConverter.GetBytes(data.RocketTemperature).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.RocketPressure).CopyTo(packet, offset);
                offset += 4;

                // Speed (float - 4 bytes)
                BitConverter.GetBytes(data.RocketSpeed).CopyTo(packet, offset);
                offset += 4;

                // Status (1 byte)
                packet[offset++] = data.status;

                // CRC hesapla ve ekle (1 byte) - Header hariç tüm data için
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, 58); // 4'den 61'e kadar (status dahil)
                packet[offset++] = calculatedCRC;

                // Padding byte (eğer gerekliyse)
                if (offset < ROCKET_PACKET_SIZE)
                {
                    packet[offset] = 0x00;
                }

                return packet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Roket paket dönüştürme hatası: {ex.Message}");
                return new byte[ROCKET_PACKET_SIZE];
            }
        }

        /// <summary>
        /// Payload telemetri verisini 34 byte binary pakete dönüştür
        /// </summary>
        private static byte[] ConvertPayloadDataToPacket(PayloadTelemetryData data)
        {
            try
            {
                byte[] packet = new byte[PAYLOAD_PACKET_SIZE];
                int offset = 0;

                // Header (4 byte): 0xCD, 0xDF, 0x14, 0x15
                Array.Copy(PAYLOAD_HEADER, 0, packet, offset, PAYLOAD_HEADER.Length);
                offset += PAYLOAD_HEADER.Length;

                // Packet Counter (1 byte)
                packet[offset++] = data.PacketCounter;

                // Altitude values (floats - 4 bytes each)
                BitConverter.GetBytes(data.PayloadAltitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.PayloadGpsAltitude).CopyTo(packet, offset);
                offset += 4;

                // GPS coordinates (floats - 4 bytes each)
                BitConverter.GetBytes(data.PayloadLatitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.PayloadLongitude).CopyTo(packet, offset);
                offset += 4;

                // Speed, Temperature, Pressure, Humidity (floats - 4 bytes each)
                BitConverter.GetBytes(data.PayloadSpeed).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.PayloadTemperature).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.PayloadPressure).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.PayloadHumidity).CopyTo(packet, offset);
                offset += 4;

                // CRC hesapla ve ekle (1 byte) - Header hariç tüm data için
                byte calculatedCRC = CalculateSimpleCRC(packet, 4, offset - 4);
                packet[offset] = calculatedCRC;

                return packet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Payload paket dönüştürme hatası: {ex.Message}");
                return new byte[PAYLOAD_PACKET_SIZE];
            }
        }
        #endregion

        /// <summary>
        /// ✨ YENİ: HYIDenem paketini parse et
        /// </summary>
        private static HYIDenemeData? ParseHYIDenemeData(byte[] packet)
        {
            try
            {
                if (!IsValidPacket(packet, HYIDENEM_HEADER))
                {
                    Debug.WriteLine("HYIDenem paketi header validation başarısız!");
                    return null;
                }

                // CRC validation
                byte expectedCRC = packet[95]; // Byte 96
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, 91); // 4'ten 94'e kadar
               
                if (calculatedCRC != expectedCRC)
                {
                    Debug.WriteLine($"HYIDenem CRC hatası! Hesaplanan: 0x{calculatedCRC:X2}, Gelen: 0x{expectedCRC:X2}");
                    // CRC hatası olsa bile veriyi döndür (test amaçlı)
                }

                var data = new HYIDenemeData
                {
                    // Temel bilgiler
                    TeamId = packet[4],                                                   // Byte 5
                    PacketCounter = packet[5],                                           // Byte 6
                    

                    // Roket telemetrileri (Byte 7-62)
                    RocketAltitude = BitConverter.ToSingle(packet, 6),                   // Byte 7-10
                    RocketGpsAltitude = BitConverter.ToSingle(packet, 10),               // Byte 11-14
                    RocketLatitude = BitConverter.ToSingle(packet, 14),                  // Byte 15-18
                    RocketLongitude = BitConverter.ToSingle(packet, 18),                 // Byte 19-22
                    GyroX = BitConverter.ToSingle(packet, 22),                           // Byte 23-26
                    GyroY = BitConverter.ToSingle(packet, 26),                           // Byte 27-30
                    GyroZ = BitConverter.ToSingle(packet, 30),                           // Byte 31-34
                    AccelX = BitConverter.ToSingle(packet, 34),                          // Byte 35-38
                    AccelY = BitConverter.ToSingle(packet, 38),                          // Byte 39-42
                    AccelZ = BitConverter.ToSingle(packet, 42),                          // Byte 43-46
                    Angle = BitConverter.ToSingle(packet, 46),                           // Byte 47-50
                    RocketSpeed = BitConverter.ToSingle(packet, 50),                     // Byte 51-54
                    RocketTemperature = BitConverter.ToSingle(packet, 54),               // Byte 55-58
                    RocketPressure = BitConverter.ToSingle(packet, 58),                  // Byte 59-62
                    RocketStatus = packet[62],                                           // Byte 63
                    

                    // Payload verileri (Byte 64-95)
                    PayloadAltitude = BitConverter.ToSingle(packet, 63),                 // Byte 64-67
                    PayloadGpsAltitude = BitConverter.ToSingle(packet, 67),              // Byte 68-71
                    PayloadLatitude = BitConverter.ToSingle(packet, 71),                 // Byte 72-75
                    PayloadLongitude = BitConverter.ToSingle(packet, 75),                // Byte 76-79
                    PayloadSpeed = BitConverter.ToSingle(packet, 79),                    // Byte 80-83
                    PayloadTemperature = BitConverter.ToSingle(packet, 83),              // Byte 84-87
                    PayloadPressure = BitConverter.ToSingle(packet, 87),                 // Byte 88-91
                    PayloadHumidity = BitConverter.ToSingle(packet, 91),                 // Byte 92-95
                    

                    CRC = packet[95]                                                     // Byte 96
                };

                Debug.WriteLine($"HYIDenem paketi parse edildi: #{data.PacketCounter}, Roket İrtifa: {data.RocketAltitude:F2}m, Payload İrtifa: {data.PayloadAltitude:F2}m");
                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HYIDenem parse hatası: {ex.Message}");
                return null;
            }
        }

        public async Task InitializeAsync(string portName, int baudRate)
        {
            try
            {
                _logger?.LogInformation("SerialPort başlatılıyor: {PortName}, {BaudRate}", portName, baudRate);

                await DisposePortAsync(_inputPort);

                _inputPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                _logger?.LogInformation("SerialPort başarıyla başlatıldı");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SerialPort başlatma hatası");
                OnError?.Invoke($"Port başlatma hatası: {ex.Message}");
                throw;
            }
        }

        // Sync wrapper for backward compatibility
        public void Initialize(string portName, int baudRate)
        {
            InitializeAsync(portName, baudRate).GetAwaiter().GetResult();
        }

        public async Task StartReadingAsync()
        {
            if (_inputPort == null)
            {
                var error = "Serial port başlatılmadan önce Initialize çağrılmalı";
                _logger?.LogError(error);
                throw new InvalidOperationException(error);
            }

            try
            {
                _inputPort.DataReceived -= SerialPort_DataReceived;
                _inputPort.DataReceived += SerialPort_DataReceived;

                if (!_inputPort.IsOpen)
                {
                    await Task.Run(() => _inputPort.Open());
                }

                // Background processing task başlat
                _processingTask = ProcessDataQueueAsync(_cancellationTokenSource.Token);

                _logger?.LogInformation("SerialPort okuma başlatıldı - Port açık: {IsOpen}", _inputPort.IsOpen);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SerialPort okuma başlatma hatası");
                OnError?.Invoke($"Port okuma hatası: {ex.Message}");
                throw;
            }
        }

        public void StartReading()
        {
            StartReadingAsync().GetAwaiter().GetResult();
        }

        private async Task ProcessDataQueueAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_dataQueue.TryDequeue(out byte[]? data))
                    {
                        lock (_bufferLock)
                        {
                            _binaryBuffer.AddRange(data);
                            

                            // Buffer overflow koruması
                            if (_binaryBuffer.Count > MAX_BUFFER_SIZE)
                            {
                                var removeCount = _binaryBuffer.Count - MAX_BUFFER_SIZE;
                                _binaryBuffer.RemoveRange(0, removeCount);
                                _logger?.LogWarning("Buffer overflow, {Count} byte silindi", removeCount);
                            }
                        }
                        
                        ProcessBinaryBuffer();
                    }
                    else
                    {
                        await Task.Delay(1, cancellationToken); // CPU kullanımını azalt
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Veri işleme hatası");
                    OnError?.Invoke($"Veri işleme hatası: {ex.Message}");
                }
            }
        }

        public async Task WriteAsync(byte[] data)
        {
            if (_inputPort == null || !_inputPort.IsOpen)
            {
                var error = "Yazma işlemi için port açık değil";
                _logger?.LogError(error);
                throw new InvalidOperationException(error);
            }

            try
            {
                await Task.Run(() => _inputPort.Write(data, 0, data.Length));
                _logger?.LogDebug("{Count} byte yazıldı", data.Length);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Veri yazma hatası");
                OnError?.Invoke($"Veri yazma hatası: {ex.Message}");
                throw;
            }
        }

        public void Write(byte[] data)
        {
            WriteAsync(data).GetAwaiter().GetResult();
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_inputPort == null) return;

                int bytesToRead = _inputPort.BytesToRead;
                if (bytesToRead == 0) return;

                byte[] tempBuffer = new byte[bytesToRead];
                int bytesRead = _inputPort.Read(tempBuffer, 0, bytesToRead);

                if (bytesRead > 0)
                {
                    // Gelen binary veriyi queue'ya ekle
                    byte[] actualData = new byte[bytesRead];
                    Array.Copy(tempBuffer, 0, actualData, 0, bytesRead);
                    _dataQueue.Enqueue(actualData);
                    
                    // Debug: Ham veri boyutunu logla
                    _logger?.LogDebug("Ham veri alındı: {BytesRead} byte", bytesRead);
                    
                    // DETAYLI HEX DUMP EKLE
                    try
                    {
                        int currentBufferSize = 0;
                        lock (_bufferLock)
                        {
                            currentBufferSize = _binaryBuffer.Count;
                        }
                        
                        // HEX formatında tam veriyi göster (ilk 32 byte)
                        int displayBytes = Math.Min(32, bytesRead);
                        string hexString = BitConverter.ToString(actualData, 0, displayBytes).Replace("-", " ");
                        
                        // Başlık kontrolü yap
                        bool hasHyiHeader = false;
                        if (bytesRead >= 4)
                        {
                            hasHyiHeader = actualData[0] == 0xFF && actualData[1] == 0xFF && 
                                          actualData[2] == 0x54 && actualData[3] == 0x52;
                        }
                        
                        string headerInfo = hasHyiHeader ? " ✅ HYI HEADER BULUNDU!" : "";
                        string completionInfo = (currentBufferSize + bytesRead) >= HYI_PACKET_SIZE ? " ✅ 78 BYTE TAMAMLANDI" : " ⏳ VERİ BEKLENİYOR";
                        
                        string dataAsString = $"📡 [{bytesRead} byte] {hexString}{headerInfo}{completionInfo} (Buffer: {currentBufferSize + bytesRead})";
                        OnDataReceived?.Invoke(dataAsString);
                        
                        // Eğer HYI header varsa daha detaylı analiz
                        if (hasHyiHeader && bytesRead >= 6)
                        {
                            OnDataReceived?.Invoke($"🔍 HYI ANALİZ: Takım ID: {actualData[4]}, Sayaç: {actualData[5]}");
                            
                            if (bytesRead >= 10)
                            {
                                float altitude = BitConverter.ToSingle(actualData, 6);
                                OnDataReceived?.Invoke($"🔍 HYI İRTİFA: {altitude:F2}m");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OnDataReceived?.Invoke($"[{bytesRead} byte] Parse hatası: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SerialPort veri alma hatası");
                OnError?.Invoke($"Veri alma hatası: {ex.Message}");
            }
        }

        // Improved packet processing with better error handling
        private void ProcessBinaryBuffer()
        {
            try
            {
                lock (_bufferLock)
                {
                    // Buffer boyutunu debug için logla
                    if (_binaryBuffer.Count > 0)
                    {
                        _logger?.LogDebug("Buffer işleniyor: {BufferSize} byte mevcut", _binaryBuffer.Count);
                        
                        // 64 byte tam paket varsa özel bilgilendirme
                        if (_binaryBuffer.Count >= ROCKET_PACKET_SIZE)
                        {
                            // İlk 16 byte'ın hex halini göster
                            string bufferHex = BitConverter.ToString(_binaryBuffer.Take(16).ToArray()).Replace("-", " ");
                            _logger?.LogDebug("🔥 TAM PAKET MEVCUT! Buffer başlangıcı: {BufferHex}...", bufferHex);
                            
                            // OnDataReceived event'i ile de bilgilendir
                            OnDataReceived?.Invoke($"🚀 TAM 64 BYTE PAKET HAZIR! Buffer: {_binaryBuffer.Count} byte - İşlenecek...");
                        }
                    }
                    
                    // PAKET İŞLEME - SADECE BİR KEZ ÇAĞIR!
                    ProcessRocketTelemetryPackets();
                    ProcessPayloadTelemetryPackets();  
                    ProcessHYIPackets();
                    // ✨ YENİ: HYIDenem paketlerini işle
                    ProcessHYIDenemePackets();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Binary buffer işleme hatası");
                OnError?.Invoke($"Paket işleme hatası: {ex.Message}");
            }
        }

        // Generic packet processor
        private void ProcessPackets<T>(byte[] header, int packetSize, 
            Func<byte[], T?> parser, Action<T> onDataReceived, string packetType) where T : class
        {
            while (_binaryBuffer.Count >= packetSize)
            {
                int headerIndex = FindHeader(_binaryBuffer, header);

                if (headerIndex == -1)
                {
                    // Header bulunamadı, buffer'dan bir byte sil ve tekrar dene
                    if (_binaryBuffer.Count > header.Length)
                    {
                        _binaryBuffer.RemoveAt(0);
                        _logger?.LogDebug("{PacketType} header bulunamadı, buffer'dan 1 byte silindi. Kalan: {Remaining}", 
                            packetType, _binaryBuffer.Count);
                    }
                    else
                        break;
                    continue;
                }

                if (headerIndex > 0)
                {
                    // Header başlangıçta değil, önceki verileri sil
                    _logger?.LogDebug("{PacketType} Header {Index} pozisyonunda bulundu, önceki {Count} byte siliniyor", 
                        packetType, headerIndex, headerIndex);
                    _binaryBuffer.RemoveRange(0, headerIndex);
                    continue;
                }

                // Header başlangıçta, tam paket var mı kontrol et
                if (_binaryBuffer.Count < packetSize)
                {
                    _logger?.LogDebug("{PacketType} için yetersiz veri: {Current}/{Required} byte", 
                        packetType, _binaryBuffer.Count, packetSize);
                    break;
                }

                // Tam paket mevcut, parse et
                byte[] packet = _binaryBuffer.GetRange(0, packetSize).ToArray();
                
                // Debug: Paket hex'ini logla
                string packetHex = BitConverter.ToString(packet, 0, Math.Min(16, packetSize)).Replace("-", " ");
                _logger?.LogDebug("{PacketType} paketi işleniyor: {PacketSize} byte - Başlangıç: {PacketHex}...", 
                    packetType, packetSize, packetHex);
                
                var telemetryData = parser(packet);

                if (telemetryData != null)
                {
                    _logger?.LogDebug("{PacketType} paketi başarıyla parse edildi", packetType);
                    Dispatcher?.TryEnqueue(() => onDataReceived(telemetryData));
                }
                else
                {
                    _logger?.LogWarning("{PacketType} paketi parse edilemedi", packetType);
                }

                _binaryBuffer.RemoveRange(0, packetSize);
                _logger?.LogDebug("{PacketType} paketi buffer'dan silindi, kalan: {Remaining} byte", 
                    packetType, _binaryBuffer.Count);
            }
        }

        private void ProcessRocketTelemetryPackets()
        {
            ProcessPackets(ROCKET_HEADER, ROCKET_PACKET_SIZE, ParseRocketData,
                data => {
                    _logger?.LogDebug("Roket paketi başarıyla parse edildi: Paket #{PacketCounter}, İrtifa: {Altitude:F2}m", 
                        data.PacketCounter, data.RocketAltitude);
                    
                    // HomePage'e başarılı parse bilgisi gönder
                    OnDataReceived?.Invoke($"✅ ROKET PAKETİ PARSE EDİLDİ! #{data.PacketCounter} - İrtifa: {data.RocketAltitude:F2}m, Hız: {data.RocketSpeed:F2}m/s");
                    
                    OnRocketDataUpdated?.Invoke(data);
                    CheckAndFireTelemetryUpdate(data, null);
                    
                    // Rotation data event'ini fırlatmak için
                    OnRotationDataReceived?.Invoke(data.GyroX, data.GyroY, data.GyroZ);
                    
                    // ✨ YENİ: Roket telemetrilerinden HYIDenem verisi oluştur ve chart'ları güncelle
                    GenerateHYIDenemeFromRocketAndUpdateCharts(data);
                    
                    // ✅ DEBUG: Arduino verilerinden HYI paketi oluşturma işlemini kontrol et
                    Debug.WriteLine($"🔍 ProcessRocketTelemetryPackets: IsAutoHyiGenerationEnabled = {IsAutoHyiGenerationEnabled}");
                    
                    // ✨ YENİ: HYI test sistemi aktifse Arduino verilerinden HYI paketi oluştur ve gönder
                    if (IsAutoHyiGenerationEnabled)
                    {
                        Debug.WriteLine($"🚀➡️📡 Arduino verilerinden HYI paketi oluşturma işlemi başlatılıyor...");
                        Task.Run(async () => GenerateAndSendHyiFromRocketData(data));
                    }
                    else
                    {
                        Debug.WriteLine($"❌ IsAutoHyiGenerationEnabled = false, HYI paketi oluşturulmayacak");
                        OnDataReceived?.Invoke("⚠️ Arduino verilerinden HYI üretimi aktif değil! HYI test modunu başlatın.");
                    }
                }, "Rocket");
        }

        private void ProcessPayloadTelemetryPackets()
        {
            ProcessPackets(PAYLOAD_HEADER, PAYLOAD_PACKET_SIZE, ParsePayloadData,
                data => {
                    _logger?.LogDebug("Payload paketi başarıyla parse edildi: Paket #{PacketCounter}, İrtifa: {Altitude:F2}m, Sıcaklık: {Temperature:F1}°C", 
                        data.PacketCounter, data.PayloadAltitude, data.PayloadTemperature);
                    
                    // HomePage'e başarılı parse bilgisi gönder
                    OnDataReceived?.Invoke($"✅ PAYLOAD PAKETİ PARSE EDİLDİ! #{data.PacketCounter} - İrtifa: {data.PayloadAltitude:F2}m, Sıcaklık: {data.PayloadTemperature:F1}°C");
                    
                    OnPayloadDataUpdated?.Invoke(data);
                    CheckAndFireTelemetryUpdate(null, data);
                }, "Payload");
        }

        private void ProcessHYIPackets()
        {
            ProcessPackets(HYI_HEADER, HYI_PACKET_SIZE, ParseHYIData, 
                data => {
                    OnHYIPacketReceived?.Invoke(data);
                    
                    // Gerçek HYI paketini output port'a forward et
                    if (IsOutputPortOpen())
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                // Gerçek HYI verisini binary paket haline getir
                                byte[] hyiPacket = ConvertHyiDataToPacket(data);
                                await WriteToOutputPortAsync(hyiPacket);
                                

                                OnDataReceived?.Invoke($"📡 GERÇEK HYI paketi forward edildi: #{data.PacketCounter} - {hyiPacket.Length} byte");
                                _logger?.LogDebug("HYI paketi başarıyla forward edildi: #{PacketCounter}", data.PacketCounter);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError(ex, "HYI paket forward hatası");
                                OnError?.Invoke($"HYI forward hatası: {ex.Message}");
                            }
                        });
                    }
                }, "HYI");
        }

        /// <summary>
        /// ✨ YENİ: HYIDenem paketlerini işle
        /// </summary>
        private void ProcessHYIDenemePackets()
        {
            ProcessPackets(HYIDENEM_HEADER, HYIDENEM_PACKET_SIZE, ParseHYIDenemeData,
                data => {
                    _logger?.LogDebug("HYIDenem paketi başarıyla parse edildi: Paket #{PacketCounter}, Roket İrtifa: {RocketAltitude:F2}m, Payload İrtifa: {PayloadAltitude:F2}m", 
                        data.PacketCounter, data.RocketAltitude, data.PayloadAltitude);
                    
                    // HomePage'e başarılı parse bilgisi gönder
                    OnDataReceived?.Invoke($"✨ HYIDenem PAKETİ PARSE EDİLDİ! #{data.PacketCounter} - Roket: {data.RocketAltitude:F2}m, Payload: {data.PayloadAltitude:F2}m");
                    
                    // Event'i tetikle (chart güncellemesi için)
                    OnHYIDenemeDataUpdated?.Invoke(data);
                    
                    // Chart'ları direkt güncelle
                    UpdateChartsFromHYIDenem(data);
                    
                    // Rotation event'ini de fırlatı
                    OnRotationDataReceived?.Invoke(data.GyroX, data.GyroY, data.GyroZ);
                }, "HYIDenem");
        }

        private static RocketTelemetryData? ParseRocketData(byte[] packet)
        {
            try
            {
                if (!IsValidPacket(packet, ROCKET_HEADER))
                {
                    Debug.WriteLine("❌ Roket paketi header validation başarısız!");
                    return null;
                }

                // ✅ TAM DEBUG BİLGİSİ - TÜM PAKETİ ANALIZ ET
                Debug.WriteLine("🔍 ====== ROKET PAKETİ PARSE EDİLİYOR ======");
                Debug.WriteLine($"📏 Paket boyutu: {packet.Length} byte");
                
                // Header kontrolü
                string headerHex = BitConverter.ToString(packet, 0, 4).Replace("-", " ");
                Debug.WriteLine($"🏷️ Header: {headerHex}");
                
                // İlk 20 byte hex dump
                string first20Hex = BitConverter.ToString(packet, 0, Math.Min(20, packet.Length)).Replace("-", " ");
                Debug.WriteLine($"🔍 İlk 20 byte: {first20Hex}");
                
                // Son 10 byte hex dump
                if (packet.Length >= 10)
                {
                    string last10Hex = BitConverter.ToString(packet, packet.Length - 10, 10).Replace("-", " ");
                    Debug.WriteLine($"🔍 Son 10 byte: {last10Hex}");
                }

                var data = new RocketTelemetryData
                {
                    PacketCounter = packet[4],
                    RocketAltitude = BitConverter.ToSingle(packet, 5),
                    RocketGpsAltitude = BitConverter.ToSingle(packet, 9),
                    RocketLatitude = BitConverter.ToSingle(packet, 13),
                    RocketLongitude = BitConverter.ToSingle(packet, 17),
                    GyroX = BitConverter.ToSingle(packet, 21),
                    GyroY = BitConverter.ToSingle(packet, 25),
                    GyroZ = BitConverter.ToSingle(packet, 29),
                    AccelX = BitConverter.ToSingle(packet, 33),
                    AccelY = BitConverter.ToSingle(packet, 37),
                    AccelZ = BitConverter.ToSingle(packet, 41),
                    Angle = BitConverter.ToSingle(packet, 45),
                    RocketTemperature = BitConverter.ToSingle(packet, 49),
                    RocketPressure = BitConverter.ToSingle(packet, 53),
                    RocketSpeed = BitConverter.ToSingle(packet, 57),
                    status = packet[61],
                    CRC = packet[62], // CRC'yi data'ya kaydet
                    TeamID = 255, // Default team ID
                };

                // ✅ DETAYLI PARSE BİLGİSİ
                Debug.WriteLine($"📊 Sayaç: {data.PacketCounter}");
                Debug.WriteLine($"📡 İrtifa: {data.RocketAltitude:F2}m (bytes 5-8)");
                Debug.WriteLine($"📡 GPS İrtifa: {data.RocketGpsAltitude:F2}m (bytes 9-12)");
                Debug.WriteLine($"🌍 Koordinat: {data.RocketLatitude:F6}, {data.RocketLongitude:F6} (bytes 13-20)");
                Debug.WriteLine($"🔄 Gyro: X={data.GyroX:F2}, Y={data.GyroY:F2}, Z={data.GyroZ:F2} (bytes 21-32)");
                Debug.WriteLine($"⚡ Accel: X={data.AccelX:F2}, Y={data.AccelY:F2}, Z={data.AccelZ:F2} (bytes 33-44)");
                Debug.WriteLine($"📐 Açı: {data.Angle:F2}° (bytes 45-48)");
                Debug.WriteLine($"🌡️ Sıcaklık: {data.RocketTemperature:F2}°C (bytes 49-52)");
                Debug.WriteLine($"🌀 Basınç: {data.RocketPressure:F2} hPa (bytes 53-56)");
                Debug.WriteLine($"🚀 Hız: {data.RocketSpeed:F2} m/s (bytes 57-60)");
                Debug.WriteLine($"📊 Status: {data.status} (byte 61)");

                // ✅ CRC HESAPLAMA VE DOĞRULAMA
                byte expectedCRC = packet[62]; // Arduino'dan gelen CRC
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, 58); // 4'den 61'e kadar (status dahil)
                
                Debug.WriteLine($"🔧 CRC Kontrolü:");
                Debug.WriteLine($"   - Gelen CRC: 0x{expectedCRC:X2} ({expectedCRC})");
                Debug.WriteLine($"   - Hesaplanan CRC: 0x{calculatedCRC:X2} ({calculatedCRC})");
                Debug.WriteLine($"   - Hesaplama aralığı: byte 4-61 ({58} byte)");
                
                if (calculatedCRC != expectedCRC)
                {
                    Debug.WriteLine($"❌ CRC HATASI! Gelen ≠ Hesaplanan");
                    
                    // CRC hesaplama debug - ilk 10 byte
                    int sum = 0;
                    Debug.WriteLine($"🔧 CRC Hesaplama Debug (ilk 10 byte):");
                    for (int i = 4; i < Math.Min(14, packet.Length); i++)
                    {
                        sum += packet[i];
                        Debug.WriteLine($"   byte[{i}] = 0x{packet[i]:X2} ({packet[i]}) -> sum = {sum}");
                    }
                    Debug.WriteLine($"   Final sum % 256 = {sum % 256} (0x{(sum % 256):X2})");
                    
                    // CRC hatası olsa bile veriyi döndür (test amaçlı)
                    Debug.WriteLine("⚠️ CRC hatası ignore ediliyor, veri parse ediliyor...");
                }
                else
                {
                    Debug.WriteLine($"✅ CRC DOĞRU!");
                }

                // ✅ DEĞER DOĞRULAMA KONTROLLERI
                if (float.IsNaN(data.RocketAltitude) || float.IsInfinity(data.RocketAltitude))
                {
                    Debug.WriteLine($"⚠️ İrtifa değeri geçersiz: {data.RocketAltitude}");
                }
                
                if (Math.Abs(data.RocketAltitude) > 50000)
                {
                    Debug.WriteLine($"⚠️ İrtifa değeri aşırı büyük: {data.RocketAltitude:F2}m");
                }

                // ✅ FLOAT BYTE ANALİZİ (İrtifa için)
                byte[] altitudeBytes = BitConverter.GetBytes(data.RocketAltitude);
                string altHex = BitConverter.ToString(altitudeBytes).Replace("-", " ");
                Debug.WriteLine($"🔍 İrtifa byte analizi: {altHex} -> {data.RocketAltitude:F2}m");

                Debug.WriteLine($"✅ Roket paketi başarıyla parse edildi!");
                Debug.WriteLine("========================================");
                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Rocket telemetry parse hatası: {ex.Message}");
                Debug.WriteLine($"📊 Exception Stack: {ex.StackTrace}");
                return null;
            }
        }

        private static PayloadTelemetryData? ParsePayloadData(byte[] packet)
        {
            try
            {
                if (!IsValidPacket(packet, PAYLOAD_HEADER))
                    return null;

                var data = new PayloadTelemetryData
                {
                    PacketCounter = packet[4],
                    PayloadAltitude = BitConverter.ToSingle(packet, 5),          // ✅ DÜZELTME: offset 5'te PayloadAltitude
                    PayloadGpsAltitude = BitConverter.ToSingle(packet, 9),       // ✅ DÜZELTME: offset 9'da PayloadGpsAltitude
                    PayloadLatitude = BitConverter.ToSingle(packet, 13),
                    PayloadLongitude = BitConverter.ToSingle(packet, 17),
                    PayloadSpeed = BitConverter.ToSingle(packet, 21),
                    PayloadTemperature = BitConverter.ToSingle(packet, 25),
                    PayloadPressure = BitConverter.ToSingle(packet, 29),
                    PayloadHumidity = BitConverter.ToSingle(packet, 33),
                    CRC = packet[37],
                };

                // CRC validation
                byte calculatedCRC = CalculateSimpleCRC(packet, 4, 33);
                if (calculatedCRC != packet[37])
                {
                    Debug.WriteLine($"Payload telemetry CRC hatası! Hesaplanan: 0x{calculatedCRC:X2}, Gelen: 0x{packet[37]:X2}");
                    // CRC hatası olsa bile veriyi döndür (test amaçlı)
                }

                Debug.WriteLine($"Payload paketi parse edildi: #{data.PacketCounter}, İrtifa: {data.PayloadAltitude:F2}m, Sıcaklık: {data.PayloadTemperature:F1}°C");
                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Payload telemetry parse hatası: {ex.Message}");
                return null;
            }
        }

        private static HYITelemetryData ParseHYIData(byte[] packet)
        {
            if (!IsValidPacket(packet, HYI_HEADER))
                throw new ArgumentException("Invalid HYI packet header");

            // CRC validation - C kodunuza uygun TOPLAMA YÖNTEMİ ile
            byte expectedCRC = packet[75]; // Byte 76
            byte calculatedCRC = CalculateChecksumAddition(packet, 4, 71); // 4'ten 74'e kadar (status dahil)
            
            if (calculatedCRC != expectedCRC)
            {
                Debug.WriteLine($"HYI CRC hatası! Hesaplanan: 0x{calculatedCRC:X2}, Gelen: 0x{expectedCRC:X2}");
                // CRC hatası olsa bile veriyi döndür (test amaçlı)
            }

            return new HYITelemetryData
            {
                TeamId = packet[4],                                                        // Byte 5
                PacketCounter = packet[5],                                                 // Byte 6
                Altitude = BitConverter.ToSingle(packet, 6),                              // Byte 7-10
                RocketGpsAltitude = BitConverter.ToSingle(packet, 10),                    // Byte 11-14
                RocketLatitude = BitConverter.ToSingle(packet, 14),                       // Byte 15-18
                RocketLongitude = BitConverter.ToSingle(packet, 18),                      // Byte 19-22
                PayloadGpsAltitude = BitConverter.ToSingle(packet, 22),                   // Byte 23-26
                PayloadLatitude = BitConverter.ToSingle(packet, 26),                      // Byte 27-30
                PayloadLongitude = BitConverter.ToSingle(packet, 30),                     // Byte 31-34
                StageGpsAltitude = BitConverter.ToSingle(packet, 34),                     // Byte 35-38
                StageLatitude = BitConverter.ToSingle(packet, 38),                        // Byte 39-42
                StageLongitude = BitConverter.ToSingle(packet, 42),                       // Byte 43-46
                GyroscopeX = BitConverter.ToSingle(packet, 46),                           // Byte 47-50
                GyroscopeY = BitConverter.ToSingle(packet, 50),                           // Byte 51-54
                GyroscopeZ = BitConverter.ToSingle(packet, 54),                           // Byte 55-58
                AccelerationX = BitConverter.ToSingle(packet, 58),                        // Byte 59-62
                AccelerationY = BitConverter.ToSingle(packet, 62),                        // Byte 63-66
                AccelerationZ = BitConverter.ToSingle(packet, 66),                        // Byte 67-70
                Angle = BitConverter.ToSingle(packet, 70),                                // Byte 71-74
                Status = packet[74],                                                       // Byte 75
                CRC = packet[75]                                                          // Byte 76
            };
        }

        public async Task StopReadingAsync()
        {
            try
            {
                // HYI test modunu durdur
                StopHyiTestMode();
                
                _cancellationTokenSource.Cancel();
                
                if (_processingTask != null)
                {
                    await _processingTask;
                }

                if (_inputPort?.IsOpen == true)
                {
                    _inputPort.DataReceived -= SerialPort_DataReceived;
                    _inputPort.Close();
                }

                _logger?.LogInformation("SerialPort okuma durduruldu");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SerialPort durdurma hatası");
            }
        }

        public void StopReading()
        {
            StopReadingAsync().GetAwaiter().GetResult();
        }

        private async Task DisposePortAsync(SerialPort? port)
        {
            if (port != null)
            {
                try
                {
                    if (port.IsOpen)
                    {
                        await Task.Run(() => port.Close());
                    }
                    port.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Port dispose hatası");
                }
            }
        }

        public bool IsPortOpen() => _inputPort?.IsOpen == true;

        public string GetPortInfo()
        {
            if (_inputPort == null)
                return "Port başlatılmamış";

            return $"Port: {_inputPort.PortName}, BaudRate: {_inputPort.BaudRate}, IsOpen: {_inputPort.IsOpen}";
        }

        public void UpdateChartsFromExternalData(float rocketAltitude, float payloadAltitude,
            float accelX, float accelY, float accelZ, float rocketSpeed, float payloadSpeed,
            float rocketTemp, float payloadTemp, float rocketPressure, float payloadPressure,
            float payloadHumidity, string source = "External",
            float rocketAccelX = 0, float rocketAccelY = 0)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                    {
                        _logger?.LogWarning("{Source}: ViewModel is null!", source);
                        return;
                    }

                    UpdateViewModelData(rocketAltitude, payloadAltitude,
                        rocketSpeed, payloadSpeed, rocketTemp, payloadTemp, rocketPressure,
                        payloadPressure, payloadHumidity, accelX, accelY, accelZ);

                    ViewModel.UpdateStatus($"{source} verisi: {DateTime.Now:HH:mm:ss}");

                    _logger?.LogDebug("{Source} tüm veriler chart'lara eklendi", source);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "{Source} chart güncelleme hatası", source);
                    OnError?.Invoke($"{source} chart güncelleme hatası: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// ✨ YENİ: HYIDenem verilerinden chart'ları güncelle
        /// Roket ve Payload verilerinin HEPSİNİ chart'lara ekle
        /// </summary>
        private void UpdateChartsFromHYIDenem(HYIDenemeData hyiDenemeData)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                    {
                        _logger?.LogWarning("UpdateChartsFromHYIDenem: ViewModel is null!");
                        return;
                    }

                    // ROKET VERİLERİNİN HEPSİNİ CHART'A EKLE
                    ViewModel.AddRocketAltitudeValue(hyiDenemeData.RocketAltitude);
                    ViewModel.addRocketAccelXValue(hyiDenemeData.AccelX);
                    ViewModel.addRocketAccelYValue(hyiDenemeData.AccelY);
                    ViewModel.addRocketAccelZValue(hyiDenemeData.AccelZ);
                    ViewModel.addRocketSpeedValue(hyiDenemeData.RocketSpeed);
                    ViewModel.addRocketTempValue(hyiDenemeData.RocketTemperature);
                    ViewModel.addRocketPressureValue(hyiDenemeData.RocketPressure);

                    // PAYLOAD VERİLERİNİN HEPSİNİ CHART'A EKLE
                    ViewModel.addPayloadAltitudeValue(hyiDenemeData.PayloadAltitude);
                    ViewModel.addPayloadSpeedValue(hyiDenemeData.PayloadSpeed);
                    ViewModel.addPayloadTempValue(hyiDenemeData.PayloadTemperature);
                    ViewModel.addPayloadPressureValue(hyiDenemeData.PayloadPressure);
                    ViewModel.addPayloadHumidityValue(hyiDenemeData.PayloadHumidity);

                    ViewModel.UpdateStatus($"HYIDenem verisi: {DateTime.Now:HH:mm:ss} - #{hyiDenemeData.PacketCounter}");

                    _logger?.LogDebug("HYIDenem chart'ları güncellendi - Roket: {RocketAlt:F2}m, Payload: {PayloadAlt:F2}m", 
                        hyiDenemeData.RocketAltitude, hyiDenemeData.PayloadAltitude);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "HYIDenem chart güncelleme hatası");
                }
            });
        }

        #region Output Port Methods (Backward Compatibility)
        public async Task InitializeOutputPortAsync(string portName, int baudRate)
        {
            try
            {
                _logger?.LogInformation("Output Port başlatılıyor: {PortName}, {BaudRate}", portName, baudRate);

                await DisposePortAsync(_outputPort);

                _outputPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                await Task.Run(() => _outputPort.Open());
                _logger?.LogInformation("Output Port başarıyla başlatıldı ve açıldı");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Output port başlatma hatası");
                OnError?.Invoke($"Output port başlatma hatası: {ex.Message}");
                throw;
            }
        }

        public async Task WriteToOutputPortAsync(byte[] data)
        {
            if (_outputPort == null || !_outputPort.IsOpen)
                throw new InvalidOperationException("Output port açık değil.");

            try
            {
                await Task.Run(() => _outputPort.Write(data, 0, data.Length));
                _logger?.LogDebug("Output port'a {Count} byte gönderildi", data.Length);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Output port yazma hatası");
                OnError?.Invoke($"Output port yazma hatası: {ex.Message}");
                throw;
            }
        }

        public bool IsOutputPortOpen() => _outputPort?.IsOpen == true;

        public async Task CloseOutputPortAsync()
        {
            try
            {
                await DisposePortAsync(_outputPort);
                _outputPort = null;
                _logger?.LogInformation("Output port kapatıldı");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Output port kapatma hatası");
            }
        }
        #endregion

        /// <summary>
        /// ✨ YENİ: Roket telemetrilerinden HYIDenem verisi oluştur ve chart'ları güncelle
        /// Roket telemetrilerinin hepsi + Payload verileri (GPS roketle aynı, diğerleri sabit ±0.05)
        /// Sadece chart'ları günceller, paket gönderimi yapmaz
        /// </summary>
        public void GenerateHYIDenemeFromRocketAndUpdateCharts(RocketTelemetryData rocketData)
        {
            try
            {
                // Payload verileri için küçük random değişimler (±0.05)
                float payloadAltitudeVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadSpeedVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadTempVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadPressureVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadHumidityVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);

                var hyiDenemeData = new HYIDenemeData
                {
                    // Temel bilgiler
                    TeamId = rocketData.TeamID,
                    PacketCounter = rocketData.PacketCounter,
                    
                    // TÜM ROKET TELEMETRİLERİ - AYNEN KOPYALA
                    RocketAltitude = rocketData.RocketAltitude,
                    RocketGpsAltitude = rocketData.RocketGpsAltitude,
                    RocketLatitude = rocketData.RocketLatitude,
                    RocketLongitude = rocketData.RocketLongitude,
                    GyroX = rocketData.GyroX,
                    GyroY = rocketData.GyroY,
                    GyroZ = rocketData.GyroZ,
                    AccelX = rocketData.AccelX,
                    AccelY = rocketData.AccelY,
                    AccelZ = rocketData.AccelZ,
                    Angle = rocketData.Angle,
                    RocketSpeed = rocketData.RocketSpeed,
                    RocketTemperature = rocketData.RocketTemperature,
                    RocketPressure = rocketData.RocketPressure,
                    RocketStatus = rocketData.status,
                    
                    // PAYLOAD VERİLERİ - GPS roketle aynı, diğerleri: sabit ±0.05
                    PayloadGpsAltitude = rocketData.RocketGpsAltitude, // Roket GPS irtifa ile aynı
                    PayloadLatitude = rocketData.RocketLatitude,       // Roket enlem ile aynı
                    PayloadLongitude = rocketData.RocketLongitude,     // Roket boylam ile aynı
                     
                    // Sabit değerler ±0.05 değişim ile
                    PayloadAltitude = BASE_PAYLOAD_ALTITUDE + payloadAltitudeVariation,
                    PayloadSpeed = BASE_PAYLOAD_SPEED + payloadSpeedVariation,
                    PayloadTemperature = BASE_PAYLOAD_TEMPERATURE + payloadTempVariation,
                    PayloadPressure = BASE_PAYLOAD_PRESSURE + payloadPressureVariation,
                    PayloadHumidity = BASE_PAYLOAD_HUMIDITY + payloadHumidityVariation,
                    
                    CRC = 0 // Otomatik hesaplanacak
                };

                // Chart'ları güncelle
                UpdateChartsFromHYIDenem(hyiDenemeData);
                
                // Event'i tetikle (UI güncellemesi için)
                Dispatcher?.TryEnqueue(() => OnHYIDenemeDataUpdated?.Invoke(hyiDenemeData));
                
                OnDataReceived?.Invoke($"✨ HYIDenem verisi oluşturuldu: #{hyiDenemeData.PacketCounter} - Roket: {hyiDenemeData.RocketAltitude:F1}m, Payload: {hyiDenemeData.PayloadAltitude:F1}m (±0.05 değişim)");
                
                _logger?.LogInformation("HYIDenem verisi oluşturuldu ve chart'lar güncellendi: TeamID={TeamId}, Counter={Counter}", 
                    hyiDenemeData.TeamId, hyiDenemeData.PacketCounter);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "HYIDenem verisi oluşturma hatası");
                OnError?.Invoke($"HYIDenem verisi hatası: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopReadingAsync();
                await DisposePortAsync(_inputPort);
                await DisposePortAsync(_outputPort);
                
                _cancellationTokenSource.Dispose();
                
                lock (_bufferLock)
                {
                    _binaryBuffer.Clear();
                }

                _logger?.LogInformation("SerialPortService dispose edildi");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Dispose hatası");
            }
        }

        private void CheckAndFireTelemetryUpdate(RocketTelemetryData? rocketData, PayloadTelemetryData? payloadData)
        {
            if (rocketData != null) _lastRocketData = rocketData;
            if (payloadData != null) _lastPayloadData = payloadData;

            // SADECE GERÇEK VERİ VARSA GÜNCELLE - DUMMY VERİ YOK!
            if (rocketData != null)
            {
                // Sadece gerçek payload verisi varsa kullan, yoksa null gönder
                var actualPayloadData = _lastPayloadData; // Gerçek payload verisi (null olabilir)
                
                // sitPage için telemetri güncellemesi - payload null olabilir
                OnTelemetryDataUpdated?.Invoke(rocketData, actualPayloadData);
                
                // Chart güncelleme - sadece roket verisi ile
                UpdateChartsRocketOnly(rocketData, actualPayloadData);
            }
            else if (payloadData != null)
            {
                // Sadece payload verisi geldi (roket yok)
                OnTelemetryDataUpdated?.Invoke(_lastRocketData, payloadData);
                UpdateChartsPayloadOnly(_lastRocketData, payloadData);
            }
        }

        private void UpdateChartsRocketOnly(RocketTelemetryData rocketTelemetry, PayloadTelemetryData? payloadTelemetry)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                    {
                        _logger?.LogWarning("UpdateChartsRocketOnly: ViewModel is null!");
                        return;
                    }

                    // SADECE ROKET VERİLERİNİ CHART'A EKLE
                    ViewModel.AddRocketAltitudeValue(rocketTelemetry.RocketAltitude);
                    ViewModel.addRocketAccelXValue(rocketTelemetry.AccelX);
                    ViewModel.addRocketAccelYValue(rocketTelemetry.AccelY);
                    ViewModel.addRocketAccelZValue(rocketTelemetry.AccelZ);
                    ViewModel.addRocketSpeedValue(rocketTelemetry.RocketSpeed);
                    ViewModel.addRocketTempValue(rocketTelemetry.RocketTemperature);
                    ViewModel.addRocketPressureValue(rocketTelemetry.RocketPressure);

                    // PAYLOAD VERİLERİNi SADECE GERÇEK VERİ VARSA EKLE
                    if (payloadTelemetry != null)
                    {
                        ViewModel.addPayloadAltitudeValue(payloadTelemetry.PayloadAltitude);
                        ViewModel.addPayloadSpeedValue(payloadTelemetry.PayloadSpeed);
                        ViewModel.addPayloadTempValue(payloadTelemetry.PayloadTemperature);
                        ViewModel.addPayloadPressureValue(payloadTelemetry.PayloadPressure);
                        ViewModel.addPayloadHumidityValue(payloadTelemetry.PayloadHumidity);
                    }
                    // PAYLOAD VERİSİ YOKSA CHART'A HİÇBİR ŞEY EKLENMİYOR (BOŞ KALACAK)

                    ViewModel.UpdateStatus($"Serial verisi: {DateTime.Now:HH:mm:ss} - Roket Paket: {rocketTelemetry.PacketCounter}");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Chart güncelleme hatası");
                }
            });
        }

        private void UpdateChartsPayloadOnly(RocketTelemetryData? rocketTelemetry, PayloadTelemetryData payloadTelemetry)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                    {
                        _logger?.LogWarning("UpdateChartsPayloadOnly: ViewModel is null!");
                        return;
                    }

                    // SADECE PAYLOAD VERİLERİNİ CHART'A EKLE
                    ViewModel.addPayloadAltitudeValue(payloadTelemetry.PayloadAltitude);
                    ViewModel.addPayloadSpeedValue(payloadTelemetry.PayloadSpeed);
                    ViewModel.addPayloadTempValue(payloadTelemetry.PayloadTemperature);
                    ViewModel.addPayloadPressureValue(payloadTelemetry.PayloadPressure);
                    ViewModel.addPayloadHumidityValue(payloadTelemetry.PayloadHumidity);

                    ViewModel.UpdateStatus($"Serial verisi: {DateTime.Now:HH:mm:ss} - Payload Paket: {payloadTelemetry.PacketCounter}");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Chart güncelleme hatası");
                }
            });
        }

        private void UpdateViewModelData(float rocketAltitude, float payloadAltitude,
            float rocketSpeed, float payloadSpeed, float rocketTemp, float payloadTemp, 
            float rocketPressure, float payloadPressure, float payloadHumidity, 
            float accelX, float accelY, float accelZ)
        {
            if (ViewModel == null)
            {
                _logger?.LogWarning("UpdateViewModelData: ViewModel is null!");
                return;
            }

            ViewModel.AddRocketAltitudeValue(rocketAltitude);
            ViewModel.addPayloadAltitudeValue(payloadAltitude);
            ViewModel.addRocketAccelXValue(accelX);
            ViewModel.addRocketAccelYValue(accelY);
            ViewModel.addRocketAccelZValue(accelZ);
            ViewModel.addRocketSpeedValue(rocketSpeed);
            ViewModel.addPayloadSpeedValue(payloadSpeed);
            ViewModel.addRocketTempValue(rocketTemp);
            ViewModel.addPayloadTempValue(payloadTemp);
            ViewModel.addRocketPressureValue(rocketPressure);
            ViewModel.addPayloadPressureValue(payloadPressure);
            ViewModel.addPayloadHumidityValue(payloadHumidity);

            _logger?.LogDebug("ViewModel güncellendi - Roket Alt: {RocketAlt:F2}, Payload Alt: {PayloadAlt:F2}", 
                rocketAltitude, payloadAltitude);
        }

        // Validation methods
        private static bool IsValidPacket(byte[] packet, byte[] expectedHeader)
        {
            if (packet.Length < expectedHeader.Length)
                return false;

            for (int i = 0; i < expectedHeader.Length; i++)
            {
                if (packet[i] != expectedHeader[i])
                    return false;
            }
            return true;
        }

        private static int FindHeader(List<byte> buffer, byte[] header)
        {
            for (int i = 0; i <= buffer.Count - header.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < header.Length; j++)
                {
                    if (buffer[i + j] != header[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// C kodunuza uygun checksum hesaplama - TOPLAMA YÖNTEMİ
        /// </summary>
        private static byte CalculateChecksumAddition(byte[] data, int offset, int length)
        {
            int sum = 0;
            for (int i = offset; i < offset + length; i++)
            {
                sum += data[i];
            }
            return (byte)(sum % 256);
        }

        private static byte CalculateSimpleCRC(byte[] data, int offset, int length)
        {
            byte crc = 0;
            for (int i = offset; i < offset + length; i++)
            {
                crc ^= data[i];
            }
            return crc;
        }

        /// <summary>
        /// Özel HYI verisi ile paket oluştur ve gönder - kullanıcıdan alınan değerlerle
        /// </summary>
        public async Task<bool> SendCustomHyiPacket(
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
            byte status)
        {
            try
            {
                var customHyiData = new HYITelemetryData
                {
                    TeamId = teamId,
                    PacketCounter = packetCounter,
                    Altitude = altitude,
                    RocketGpsAltitude = rocketGpsAltitude,
                    RocketLatitude = rocketLatitude,
                    RocketLongitude = rocketLongitude,
                    PayloadGpsAltitude = payloadGpsAltitude,
                    PayloadLatitude = payloadLatitude,
                    PayloadLongitude = payloadLongitude,
                    StageGpsAltitude = stageGpsAltitude,
                    StageLatitude = stageLatitude,
                    StageLongitude = stageLongitude,
                    GyroscopeX = gyroscopeX,
                    GyroscopeY = gyroscopeY,
                    GyroscopeZ = gyroscopeZ,
                    AccelerationX = accelerationX,
                    AccelerationY = accelerationY,
                    AccelerationZ = accelerationZ,
                    Angle = angle,
                    Status = status,
                    CRC = 0 // CRC otomatik hesaplanacak
                };

                byte[] packet = ConvertHyiDataToPacket(customHyiData);
                
                if (IsOutputPortOpen())
                {
                    await WriteToOutputPortAsync(packet);
                    
                    // Detaylı paket bilgisi göster
                    string packetInfo = $"📤 ÖZEL HYI PAKETİ GÖNDERİLDİ:\n" +
                                      $"Takım ID: {teamId}, Sayaç: {packetCounter}\n" +
                                      $"İrtifa: {altitude:F1}m, Roket GPS İrtifa: {rocketGpsAltitude:F1}m\n" +
                                      $"Roket Pos: {rocketLatitude:F6}, {rocketLongitude:F6}\n" +
                                      $"Payload GPS İrtifa: {payloadGpsAltitude:F1}m\n" +
                                      $"Durum: {status}, Paket Boyutu: {packet.Length} byte";
                    
                    OnDataReceived?.Invoke(packetInfo);
                    
                    // TAM PAKET HEX DUMP - TÜM PAKETİ GÖSTER
                    string fullHexString = BitConverter.ToString(packet).Replace("-", " ");
                    OnDataReceived?.Invoke($"📋 TAM PAKET (hex): {fullHexString}");
                    
                    // BYTE DETAYLARINI GÖSTER
                    OnDataReceived?.Invoke($"📍 HEADER: {packet[0]:X2} {packet[1]:X2} {packet[2]:X2} {packet[3]:X2}");
                    OnDataReceived?.Invoke($"📍 TAKIM ID: {packet[4]} (0x{packet[4]:X2})");
                    OnDataReceived?.Invoke($"📍 SAYAÇ: {packet[5]} (0x{packet[5]:X2})");
                    OnDataReceived?.Invoke($"📍 İRTİFA: {BitConverter.ToSingle(packet, 6):F2}m");
                    OnDataReceived?.Invoke($"📍 CRC: 0x{packet[75]:X2}");
                    OnDataReceived?.Invoke($"📍 SON BYTES: 0x{packet[76]:X2} 0x{packet[77]:X2} (CR LF)");
                    
                    _logger?.LogInformation("Özel HYI paketi gönderildi: TeamID={TeamId}, Counter={Counter}, Size={Size}", 
                        teamId, packetCounter, packet.Length);
                    return true;
                }
                else
                {
                    OnDataReceived?.Invoke("❌ Output port açık değil - HYI paketi gönderilemedi");
                    OnError?.Invoke("Output port açık değil");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Özel HYI paketi gönderme hatası");
                OnError?.Invoke($"Özel HYI paketi hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test amaçlı sıfır değerlerle HYI paketi gönder (DOĞRULAMA KODU test için)
        /// </summary>
        public async Task<bool> SendZeroValueHyiPacket()
        {
            return await SendCustomHyiPacket(
                teamId: 0,
                packetCounter: 0,
                altitude: 0,
                rocketGpsAltitude: 0,
                rocketLatitude: 0,
                rocketLongitude: 0,
                payloadGpsAltitude: 0,
                payloadLatitude: 0,
                payloadLongitude: 0,
                stageGpsAltitude: 0,
                stageLatitude: 0,
                stageLongitude: 0,
                gyroscopeX: 0,
                gyroscopeY: 0,
                gyroscopeZ: 0,
                accelerationX: 0,
                accelerationY: 0,
                accelerationZ: 0,
                angle: 0,
                status: 0
            );
        }

        /// <summary>
        /// ✨ YENİ: HYIDenem telemetri verisini 98 byte binary pakete dönüştür
        /// Roket telemetrilerinin hepsi + Payload verileri
        /// </summary>
        private static byte[] ConvertHYIDenemeDataToPacket(HYIDenemeData data)
        {
            try
            {
                byte[] packet = new byte[HYIDENEM_PACKET_SIZE];
                int offset = 0;

                // Byte 1-4: Header (0xDE, 0xAD, 0xBE, 0xEF)
                packet[offset++] = 0xDE;
                packet[offset++] = 0xAD;
                packet[offset++] = 0xBE;
                packet[offset++] = 0xEF;

                // Byte 5: TAKIM ID
                packet[offset++] = data.TeamId;

                // Byte 6: PAKET SAYAÇ
                packet[offset++] = data.PacketCounter;

                // ROKET TELEMETRİLERİ (Byte 7-62)
                BitConverter.GetBytes(data.RocketAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketGpsAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketLatitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketLongitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroX).CopyTo(packet, offset); offset += 4;  // ✅ DÜZELTME
                BitConverter.GetBytes(data.GyroY).CopyTo(packet, offset); offset += 4;  // ✅ DÜZELTME
                BitConverter.GetBytes(data.GyroZ).CopyTo(packet, offset); offset += 4;  // ✅ DÜZELTME
                BitConverter.GetBytes(data.AccelX).CopyTo(packet, offset); offset += 4; // ✅ DÜZELTME
                BitConverter.GetBytes(data.AccelY).CopyTo(packet, offset); offset += 4; // ✅ DÜZELTME
                BitConverter.GetBytes(data.AccelZ).CopyTo(packet, offset); offset += 4; // ✅ DÜZELTME
                BitConverter.GetBytes(data.Angle).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketSpeed).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketTemperature).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketPressure).CopyTo(packet, offset); offset += 4;

                // Roket durumu (Byte 63)
                packet[offset++] = data.RocketStatus;

                // PAYLOAD VERİLERİ (Byte 64-95)
                BitConverter.GetBytes(data.PayloadAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadGpsAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadLatitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadLongitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadSpeed).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadTemperature).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadPressure).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadHumidity).CopyTo(packet, offset); offset += 4;

                // Byte 96: CRC - Header hariç tüm data için
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, offset - 4);
                packet[offset++] = calculatedCRC;

                // Byte 97: 0x0D (CR)
                packet[offset++] = 0x0D;

                // Byte 98: 0x0A (LF)
                packet[offset++] = 0x0A;

                Debug.WriteLine($"HYIDenem paketi oluşturuldu: {packet.Length} byte, offset: {offset}");
                Debug.WriteLine($"Header: {packet[0]:X2} {packet[1]:X2} {packet[2]:X2} {packet[3]:X2}");
                Debug.WriteLine($"Takım ID: {packet[4]}, Sayaç: {packet[5]}");
                Debug.WriteLine($"Roket İrtifa: {BitConverter.ToSingle(packet, 6):F2}m");
                Debug.WriteLine($"Payload İrtifa: {BitConverter.ToSingle(packet, 63):F2}m");
                Debug.WriteLine($"CRC: 0x{packet[95]:X2}, CR LF: 0x{packet[96]:X2} 0x{packet[97]:X2}");

                return packet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HYIDenem paket dönüştürme hatası: {ex.Message}");
                return new byte[HYIDENEM_PACKET_SIZE];
            }
        }
        
        /// <summary>
        /// ✨ YENİ: Sadece Arduino verilerinden HYI üretimi aktif et (random HYI paketi üretimi olmadan)
        /// </summary>
        public void EnableAutoHyiGenerationOnly()
        {
            IsAutoHyiGenerationEnabled = true;
            OnDataReceived?.Invoke("🚀➡️📡 Arduino verilerinden HYI üretimi AKTİF EDİLDİ!");
            
            Debug.WriteLine($"🔧 Sadece Arduino->HYI üretimi aktif edildi:");
            Debug.WriteLine($"   - IsAutoHyiGenerationEnabled: {IsAutoHyiGenerationEnabled}");
            Debug.WriteLine($"   - IsHyiTestMode: {IsHyiTestMode}");
            
            _logger?.LogInformation("Arduino verilerinden HYI üretimi aktif edildi");
        }

        /// <summary>
        /// ✨ YENİ: Arduino verilerinden HYI üretimini durdur
        /// </summary>
        public void DisableAutoHyiGeneration()
        {
            IsAutoHyiGenerationEnabled = false;
            OnDataReceived?.Invoke("🛑 Arduino verilerinden HYI üretimi DURDURULDU!");
            
            Debug.WriteLine($"🔧 Arduino->HYI üretimi durduruldu:");
            Debug.WriteLine($"   - IsAutoHyiGenerationEnabled: {IsAutoHyiGenerationEnabled}");
            
            _logger?.LogInformation("Arduino verilerinden HYI üretimi durduruldu");
        }
    }
}