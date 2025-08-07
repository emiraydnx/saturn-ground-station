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
        
        private const int MAX_BUFFER_SIZE = 4096; // Buffer overflow koruması

        // TEST HYI VERİSİ İÇİN RANDOM GENERATORİ
        private readonly Random _random = new Random();
        private Timer? _hyiTestTimer;
        private byte _testHyiPacketCounter = 0;
        public bool IsHyiTestMode { get; set; } = false; // TEST MODU AÇMA/KAPAMA

        public SerialPortService(ILogger<SerialPortService>? logger = null)
        {
            _logger = logger;
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
        #endregion

        // Helper methods
        private RocketTelemetryData? _lastRocketData;
        private PayloadTelemetryData? _lastPayloadData;

        #region HYI TEST MOD METODLARİ
        
        /// <summary>
        /// HYI test modunu başlat - Random verilerle HYI paketleri oluşturur
        /// </summary>
        public void StartHyiTestMode(int intervalMs = 2000)
        {
            IsHyiTestMode = true;
            _testHyiPacketCounter = 0;
            
            _hyiTestTimer = new Timer(GenerateRandomHyiData, null, 1000, intervalMs);
            OnDataReceived?.Invoke($"🧪 HYI TEST MODU BAŞLATILDI! {intervalMs}ms aralıklarla random veri üretiliyor...");
            
            _logger?.LogInformation("HYI Test Modu başlatıldı - Interval: {IntervalMs}ms", intervalMs);
        }

        /// <summary>
        /// HYI test modunu durdur
        /// </summary>
        public void StopHyiTestMode()
        {
            IsHyiTestMode = false;
            _hyiTestTimer?.Dispose();
            _hyiTestTimer = null;
            
            OnDataReceived?.Invoke("🛑 HYI TEST MODU DURDURULDU!");
            _logger?.LogInformation("HYI Test Modu durduruldu");
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
                
                // HYI verisini binary paket haline getir ve output port'a gönder
                byte[] hyiPacket = ConvertHyiDataToPacket(hyiData);
                if (IsOutputPortOpen() && hyiPacket != null)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await WriteToOutputPortAsync(hyiPacket);
                            OnDataReceived?.Invoke($"📤 HYI paketi gönderildi: {hyiPacket.Length} byte - #{hyiData.PacketCounter}");
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "HYI paket gönderme hatası");
                            OnError?.Invoke($"HYI paket gönderme hatası: {ex.Message}");
                        }
                    });
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
        /// HYI telemetri verisini 78 byte binary pakete dönüştür
        /// </summary>
        private static byte[] ConvertHyiDataToPacket(HYITelemetryData data)
        {
            try
            {
                byte[] packet = new byte[HYI_PACKET_SIZE];
                int offset = 0;

                // Header (4 byte): 0xFF, 0xFF, 0x54, 0x52
                Array.Copy(HYI_HEADER, 0, packet, offset, HYI_HEADER.Length);
                offset += HYI_HEADER.Length;

                // Team ID (1 byte)
                packet[offset++] = data.TeamId;

                // Packet Counter (1 byte)
                packet[offset++] = data.PacketCounter;

                // Altitude values (floats - 4 bytes each)
                BitConverter.GetBytes(data.Altitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.RocketGpsAltitude).CopyTo(packet, offset);
                offset += 4;

                // Rocket GPS coordinates (floats - 4 bytes each)
                BitConverter.GetBytes(data.RocketLatitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.RocketLongitude).CopyTo(packet, offset);
                offset += 4;

                // Payload GPS coordinates (floats - 4 bytes each)
                BitConverter.GetBytes(data.PayloadGpsAltitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.PayloadLatitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.PayloadLongitude).CopyTo(packet, offset);
                offset += 4;

                // Stage GPS coordinates (floats - 4 bytes each)
                BitConverter.GetBytes(data.StageGpsAltitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.StageLatitude).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.StageLongitude).CopyTo(packet, offset);
                offset += 4;

                // Gyroscope values (floats - 4 bytes each)
                BitConverter.GetBytes(data.GyroscopeX).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.GyroscopeY).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.GyroscopeZ).CopyTo(packet, offset);
                offset += 4;

                // Acceleration values (floats - 4 bytes each)
                BitConverter.GetBytes(data.AccelerationX).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.AccelerationY).CopyTo(packet, offset);
                offset += 4;
                BitConverter.GetBytes(data.AccelerationZ).CopyTo(packet, offset);
                offset += 4;

                // Angle (float - 4 bytes)
                BitConverter.GetBytes(data.Angle).CopyTo(packet, offset);
                offset += 4;

                // Status (1 byte)
                packet[offset++] = data.Status;

                // CRC hesapla ve ekle (1 byte) - Header hariç tüm data için
                byte calculatedCRC = CalculateSimpleCRC(packet, 4, offset - 4);
                packet[offset] = calculatedCRC;

                // Son 2 byte için padding ekle (toplam 78 byte'a ulaşmak için)
                // offset şu anda 76 olmalı, 2 byte daha lazım
                if (offset < HYI_PACKET_SIZE - 1)
                {
                    packet[HYI_PACKET_SIZE - 1] = 0x00; // Padding byte
                }

                return packet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HYI paket dönüştürme hatası: {ex.Message}");
                return new byte[HYI_PACKET_SIZE]; // Boş paket döndür
            }
        }
        #endregion

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
                    // Gelen binary veriyi queue'ya ekle - SADECE BİR KEZ!
                    byte[] actualData = new byte[bytesRead];
                    Array.Copy(tempBuffer, 0, actualData, 0, bytesRead);
                    _dataQueue.Enqueue(actualData);
                    
                    // Debug: Ham veri boyutunu logla
                    _logger?.LogDebug("Ham veri alındı: {BytesRead} byte", bytesRead);
                    
                    // OnDataReceived event'i için TAM PAKET bilgisi
                    try
                    {
                        // Buffer durumunu da göster
                        int currentBufferSize = 0;
                        lock (_bufferLock)
                        {
                            currentBufferSize = _binaryBuffer.Count;
                        }
                        
                        // 64 byte tamamlanıp tamamlanmadığını kontrol et
                        bool isCompletePacket = (currentBufferSize + bytesRead) >= ROCKET_PACKET_SIZE;
                        
                        if (bytesRead <= 64) // Roket paketi boyutuna uygun
                        {
                            var hexString = BitConverter.ToString(actualData, 0, bytesRead).Replace("-", " ");
                            
                            // Paket tamamlanma durumunu da göster
                            string completionInfo = isCompletePacket ? " ✅ PAKET TAMAMLANDI" : " ⏳ PAKET BEKLENİYOR";
                            string dataAsString = $"[{bytesRead} byte] {hexString}{completionInfo} (Buffer: {currentBufferSize + bytesRead})";
                            OnDataReceived?.Invoke(dataAsString);
                        }
                        else
                        {
                            OnDataReceived?.Invoke($"[{bytesRead} byte binary data] Buffer: {currentBufferSize + bytesRead}");
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
                }, "Rocket");
        }

        private void ProcessPayloadTelemetryPackets()
        {
            ProcessPackets(PAYLOAD_HEADER, PAYLOAD_PACKET_SIZE, ParsePayloadData,
                data => {
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

        private static RocketTelemetryData? ParseRocketData(byte[] packet)
        {
            try
            {
                if (!IsValidPacket(packet, ROCKET_HEADER))
                {
                    Debug.WriteLine("Roket paketi header validation başarısız!");
                    return null;
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
                    CRC = packet[62],
                    TeamID = 255,
                };

                // CRC validation
                byte calculatedCRC = CalculateSimpleCRC(packet, 4, 58); // 4'den 61'e kadar (status dahil)
                if (calculatedCRC != packet[62])
                {
                    Debug.WriteLine($"Rocket telemetry CRC hatası! Hesaplanan: 0x{calculatedCRC:X2}, Gelen: 0x{packet[62]:X2}");
                    // CRC hatası olsa bile veriyi döndür (test amaçlı)
                }

                Debug.WriteLine($"Roket paketi parse edildi: #{data.PacketCounter}, İrtifa: {data.RocketAltitude:F2}m, CRC: 0x{data.CRC:X2}");
                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Rocket telemetry parse hatası: {ex.Message}");
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
                    PayloadAltitude = BitConverter.ToSingle(packet, 5),
                    PayloadGpsAltitude = BitConverter.ToSingle(packet, 9),
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
                    Debug.WriteLine("Payload telemetry CRC hatası!");
                }

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

            return new HYITelemetryData
            {
                TeamId = packet[4],
                PacketCounter = packet[5],
                Altitude = BitConverter.ToSingle(packet, 6),
                RocketGpsAltitude = BitConverter.ToSingle(packet, 10),
                RocketLatitude = BitConverter.ToSingle(packet, 14),
                RocketLongitude = BitConverter.ToSingle(packet, 18),
                PayloadGpsAltitude = BitConverter.ToSingle(packet, 22),
                PayloadLatitude = BitConverter.ToSingle(packet, 26),
                PayloadLongitude = BitConverter.ToSingle(packet, 30),
                StageGpsAltitude = BitConverter.ToSingle(packet, 34),
                StageLatitude = BitConverter.ToSingle(packet, 38),
                StageLongitude = BitConverter.ToSingle(packet, 42),
                GyroscopeX = BitConverter.ToSingle(packet, 46),
                GyroscopeY = BitConverter.ToSingle(packet, 50),
                GyroscopeZ = BitConverter.ToSingle(packet, 54),
                AccelerationX = BitConverter.ToSingle(packet, 58),
                AccelerationY = BitConverter.ToSingle(packet, 62),
                AccelerationZ = BitConverter.ToSingle(packet, 66),
                Angle = BitConverter.ToSingle(packet, 70),
                Status = packet[74],
                CRC = packet[75]
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

                    // PAYLOAD VERİLERİNİ SADECE GERÇEK VERİ VARSA EKLE
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

        private static byte CalculateChecksum(byte[] data, int offset, int length)
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
    }
}