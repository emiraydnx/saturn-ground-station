using copilot_deneme.ViewModels;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace copilot_deneme
{
    /// <summary>
    /// Singleton SerialPortService Manager - Tüm sayfalar arasında paylaşılan tek instance
    /// </summary>
    public sealed class SerialPortManager
    {
        private static SerialPortManager? _instance;
        private static readonly object _lock = new object();
        
        private SerialPortService? _serialPortService;
        private readonly Dictionary<string, List<Action<SerialPortService.RocketTelemetryData>>> _telemetrySubscribers = new();
        private readonly Dictionary<string, List<Action<SerialPortService.PayloadTelemetryData>>> _payloadSubscribers = new();
        private readonly Dictionary<string, List<Action<string>>> _dataSubscribers = new();
        private readonly Dictionary<string, List<Action<float, float, float>>> _rotationSubscribers = new();
        
        private SerialPortManager() { }
        
        public static SerialPortManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new SerialPortManager();
                    }
                }
                return _instance;
            }
        }
        
        public SerialPortService? SerialPortService => _serialPortService;
        public bool IsConnected => _serialPortService?.IsPortOpen() == true;
        
        public async Task InitializeAsync(string portName, int baudRate, ChartViewModel viewModel, DispatcherQueue dispatcher)
        {
            try
            {
                if (_serialPortService != null)
                {
                    await _serialPortService.DisposeAsync();
                }
                
                _serialPortService = new SerialPortService();
                _serialPortService.ViewModel = viewModel;
                _serialPortService.Dispatcher = dispatcher;
                
                // Internal event handler'ları bağla
                _serialPortService.OnTelemetryDataUpdated += OnInternalTelemetryDataUpdated;
                _serialPortService.OnPayloadDataUpdated += OnInternalPayloadDataUpdated;
                _serialPortService.OnDataReceived += OnInternalDataReceived;
                _serialPortService.OnRotationDataReceived += OnInternalRotationDataReceived;
                
                await _serialPortService.InitializeAsync(portName, baudRate);
                await _serialPortService.StartReadingAsync();
                
                LogDebug($"SerialPortManager: Port bağlandı {portName} @ {baudRate}");
            }
            catch (Exception ex)
            {
                LogDebug($"SerialPortManager başlatma hatası: {ex.Message}");
                throw;
            }
        }
        
        public async Task DisconnectAsync()
        {
            try
            {
                if (_serialPortService != null)
                {
                    await _serialPortService.StopReadingAsync();
                    LogDebug("SerialPortManager: Bağlantı kesildi");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"SerialPortManager kapatma hatası: {ex.Message}");
            }
        }
        
        // Subscriber management
        public void SubscribeToTelemetryData(string pageId, Action<SerialPortService.RocketTelemetryData> callback)
        {
            if (!_telemetrySubscribers.ContainsKey(pageId))
                _telemetrySubscribers[pageId] = new List<Action<SerialPortService.RocketTelemetryData>>();
            
            _telemetrySubscribers[pageId].Add(callback);
            LogDebug($"SerialPortManager: {pageId} telemetri verilerine abone oldu");
        }

        public void SubscribeToPayloadData(string pageId, Action<SerialPortService.PayloadTelemetryData> callback)
        {
            if (!_payloadSubscribers.ContainsKey(pageId))
                _payloadSubscribers[pageId] = new List<Action<SerialPortService.PayloadTelemetryData>>();

            _payloadSubscribers[pageId].Add(callback);
            LogDebug($"SerialPortManager: {pageId} payload verilerine abone oldu");
        }
        
        public void SubscribeToDataReceived(string pageId, Action<string> callback)
        {
            if (!_dataSubscribers.ContainsKey(pageId))
                _dataSubscribers[pageId] = new List<Action<string>>();
            
            _dataSubscribers[pageId].Add(callback);
            LogDebug($"SerialPortManager: {pageId} ham veriye abone oldu");
        }
        
        public void SubscribeToRotationData(string pageId, Action<float, float, float> callback)
        {
            if (!_rotationSubscribers.ContainsKey(pageId))
                _rotationSubscribers[pageId] = new List<Action<float, float, float>>();
            
            _rotationSubscribers[pageId].Add(callback);
            LogDebug($"SerialPortManager: {pageId} rotation verilerine abone oldu");
        }
        
        public void UnsubscribeAll(string pageId)
        {
            _telemetrySubscribers.Remove(pageId);
            _payloadSubscribers.Remove(pageId);
            _dataSubscribers.Remove(pageId);
            _rotationSubscribers.Remove(pageId);
            LogDebug($"SerialPortManager: {pageId} tüm aboneliklerden çıkarıldı");
        }
        
        // Internal event handlers - optimize edildi
        private void OnInternalTelemetryDataUpdated(SerialPortService.RocketTelemetryData data)
        {
            // Event dağıtımı optimize edildi - LINQ yerine foreach
            foreach (var subscriberList in _telemetrySubscribers.Values)
            {
                foreach (var callback in subscriberList)
                {
                    try
                    {
                        callback(data);
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"SerialPortManager telemetri callback hatası: {ex.Message}");
                    }
                }
            }
        }

        private void OnInternalPayloadDataUpdated(SerialPortService.PayloadTelemetryData data)
        {
            foreach (var subscriberList in _payloadSubscribers.Values)
            {
                foreach (var callback in subscriberList)
                {
                    try
                    {
                        callback(data);
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"SerialPortManager payload callback hatası: {ex.Message}");
                    }
                }
            }
        }
        
        private void OnInternalDataReceived(string data)
        {
            // Sadece gerekirse ham veri event'ini tetikle
            if (_dataSubscribers.Count > 0)
            {
                foreach (var subscriberList in _dataSubscribers.Values)
                {
                    foreach (var callback in subscriberList)
                    {
                        try
                        {
                            callback(data);
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"SerialPortManager data callback hatası: {ex.Message}");
                        }
                    }
                }
            }
        }
        
        private void OnInternalRotationDataReceived(float x, float y, float z)
        {
            // Sadece rotation subscriber'ları varsa çalıştır
            if (_rotationSubscribers.Count > 0)
            {
                foreach (var subscriberList in _rotationSubscribers.Values)
                {
                    foreach (var callback in subscriberList)
                    {
                        try
                        {
                            callback(x, y, z);
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"SerialPortManager rotation callback hatası: {ex.Message}");
                        }
                    }
                }
            }
        }
        
        public string GetConnectionInfo()
        {
            return _serialPortService?.GetPortInfo() ?? "Bağlantı yok";
        }
        
        // Conditional Debug Logging - RELEASE'de hiç çalışmaz
        [Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
    
    public class SerialPortService : ISerialPortService
    {
        private readonly ILogger<SerialPortService>? _logger;
        private SerialPort? _inputPort;
        private readonly List<byte> _buffer = new();
        private readonly object _bufferLock = new();
        private Task? _processingTask;
        private CancellationTokenSource? _cancellationTokenSource;
        
        private const int ROCKET_PACKET_SIZE = 70;
        private static readonly byte[] ROCKET_HEADER = { 0xAB, 0xBC, 0x12, 0x13 };
        private static readonly byte[] ROCKET_FOOTER = { 0xEE, 0xFF };

        private const int PAYLOAD_PACKET_SIZE = 40;
        private static readonly byte[] PAYLOAD_HEADER = { 0xCD, 0xDE, 0x14, 0x15 };
        private static readonly byte[] PAYLOAD_FOOTER = { 0xDD, 0xCC };

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
        public event Action<RocketTelemetryData>? OnRocketDataUpdated;
        public event Action<float, float, float>? OnRotationDataReceived;
        public event Action<RocketTelemetryData>? OnTelemetryDataUpdated;
        public event Action<string>? OnError;

        // Interface uyumluluğu için - kullanılmıyor
        public event Action<PayloadTelemetryData>? OnPayloadDataUpdated;
        public event Action<HYITelemetryData>? OnHYIPacketReceived;
        #endregion

        #region Data Classes
        public class RocketTelemetryData
        {
            public byte PacketCounter { get; set; }
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
            public float RocketTemperature { get; set; }
            public float RocketPressure { get; set; }
            public float RocketSpeed { get; set; }
            public float DpDt { get; set; } 
            public byte Status { get; set; }
            public byte CRC { get; set; }
            public byte TeamID { get; set; }
        }

        // Payload veri yapısı güncellendi
        public class PayloadTelemetryData
        {
            public byte PacketCounter { get; set; }
            public float Altitude { get; set; }
            public float GpsAltitude { get; set; }
            public float Latitude { get; set; }
            public float Longitude { get; set; }
            public float Temperature { get; set; }
            public float Humidity { get; set; }
            public float Pressure { get; set; }
            public float PayloadSpeed { get; set; } 
            public byte Checksum { get; set; }
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

        public async Task InitializeAsync(string portName, int baudRate)
        {
            try
            {
                _logger?.LogInformation("SerialPort başlatılıyor: {PortName}, {BaudRate}", portName, baudRate);

                if (_inputPort != null)
                {
                    await DisposePortAsync();
                }

                _inputPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    // Veri alımını daha verimli hale getirmek için tampon boyutunu artırın
                    ReadBufferSize = 4096,
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
                if (!_inputPort.IsOpen)
                {
                    await Task.Run(() => _inputPort.Open());
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _processingTask = Task.Run(() => ProcessBufferLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                _inputPort.DataReceived -= SerialPort_DataReceived;
                _inputPort.DataReceived += SerialPort_DataReceived;

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

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_inputPort == null || !_inputPort.IsOpen) return;

                int bytesToRead = _inputPort.BytesToRead;
                if (bytesToRead == 0) return;

                byte[] tempBuffer = new byte[bytesToRead];
                int bytesRead = _inputPort.Read(tempBuffer, 0, bytesToRead);

                if (bytesRead > 0)
                {
                    lock (_bufferLock)
                    {
                        _buffer.AddRange(tempBuffer.Take(bytesRead));
                    }
                    // Debug bilgisi - SADECE DEBUG modunda
                    LogDebugData(tempBuffer, bytesRead);
                }
            }
            catch (Exception ex)
            {
                // Portun kapanması gibi durumlarda oluşabilecek hataları yoksay
                if (ex is InvalidOperationException || ex is OperationCanceledException)
                {
                    return;
                }
                _logger?.LogError(ex, "SerialPort veri alma hatası");
                OnError?.Invoke($"Veri alma hatası: {ex.Message}");
            }
        }

        private async void ProcessBufferLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    ProcessBuffer();
                    // CPU kullanımını düşürmek için küçük bir bekleme ekleyin
                    await Task.Delay(20, token);
                }
                catch (OperationCanceledException)
                {
                    // Görev iptal edildiğinde döngüden çık
                    break;
                }
                catch (Exception ex)
                {
                    LogDebug($"ProcessBufferLoop hatası: {ex.Message}");
                }
            }
        }

        private void ProcessBuffer()
        {
            lock (_bufferLock)
            {
                // Buffer'da işlenecek veri olduğu sürece döngüye devam et
                while (true)
                {
                    // En küçük paket boyutundan az veri varsa çık (40 byte)
                    if (_buffer.Count < PAYLOAD_PACKET_SIZE) 
                    {
                        break;
                    }

                    int rocketHeaderIndex = FindHeader(_buffer, ROCKET_HEADER);
                    int payloadHeaderIndex = FindHeader(_buffer, PAYLOAD_HEADER);

                    // Hiç header bulunamadıysa
                    if (rocketHeaderIndex == -1 && payloadHeaderIndex == -1)
                    {
                        // Olası bir header'ın başlangıcını korumak için son birkaç byte hariç temizle
                        int bytesToRemove = _buffer.Count - Math.Max(ROCKET_HEADER.Length, PAYLOAD_HEADER.Length);
                        if (bytesToRemove > 0)
                        {
                            _buffer.RemoveRange(0, bytesToRemove);
                        }
                        break; // Döngüden çık, yeni veri bekle
                    }

                    // Hangi header daha önce geliyorsa onu işle
                    if (rocketHeaderIndex != -1 && (payloadHeaderIndex == -1 || rocketHeaderIndex <= payloadHeaderIndex))
                    {
                        // Header'dan önceki bozuk verileri temizle
                        if (rocketHeaderIndex > 0)
                        {
                            _buffer.RemoveRange(0, rocketHeaderIndex);
                        }

                        // Paketin tamamının buffer'da olup olmadığını kontrol et
                        if (_buffer.Count < ROCKET_PACKET_SIZE)
                        {
                            break; // Paketin geri kalanını bekle
                        }

                        byte[] packet = _buffer.GetRange(0, ROCKET_PACKET_SIZE).ToArray();
                        var telemetryData = ParseRocketData(packet);

                        if (telemetryData != null)
                        {
                            Dispatcher?.TryEnqueue(() =>
                            {
                                OnRocketDataUpdated?.Invoke(telemetryData);
                                OnTelemetryDataUpdated?.Invoke(telemetryData);
                                OnRotationDataReceived?.Invoke(telemetryData.GyroX, telemetryData.GyroY, telemetryData.GyroZ);
                                UpdateCharts(telemetryData);
                            });
                        }
                        // İşlenen paketi buffer'dan sil
                        _buffer.RemoveRange(0, ROCKET_PACKET_SIZE);
                    }
                    else if (payloadHeaderIndex != -1)
                    {
                        // Header'dan önceki bozuk verileri temizle
                        if (payloadHeaderIndex > 0)
                        {
                            _buffer.RemoveRange(0, payloadHeaderIndex);
                        }

                        // Paketin tamamının buffer'da olup olmadığını kontrol et
                        if (_buffer.Count < PAYLOAD_PACKET_SIZE)
                        {
                            break; // Paketin geri kalanını bekle
                        }

                        byte[] packet = _buffer.GetRange(0, PAYLOAD_PACKET_SIZE).ToArray();
                        var payloadData = ParsePayloadData(packet);

                        if (payloadData != null)
                        {
                            Dispatcher?.TryEnqueue(() =>
                            {
                                OnPayloadDataUpdated?.Invoke(payloadData);
                                UpdateCharts(payloadData);
                            });
                        }
                        // İşlenen paketi buffer'dan sil
                        _buffer.RemoveRange(0, PAYLOAD_PACKET_SIZE);
                    }
                    else
                    {
                        // Bu duruma normalde girilmemeli, döngüyü kır
                        break;
                    }
                }
            }
        }

        private int FindHeader(List<byte> buffer, byte[] headerToFind)
        {
            for (int i = 0; i <= buffer.Count - headerToFind.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < headerToFind.Length; j++)
                {
                    if (buffer[i + j] != headerToFind[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private RocketTelemetryData? ParseRocketData(byte[] packet)
        {
            LogDebug($"ParseRocketData çağrıldı, paket boyutu: {packet.Length}");
            try
            {
                if (packet.Length != ROCKET_PACKET_SIZE)
                    return null;

                // Footer kontrol (paket sonunda)
                for (int i = 0; i < ROCKET_FOOTER.Length; i++)
                {
                    if (packet[packet.Length - ROCKET_FOOTER.Length + i] != ROCKET_FOOTER[i])
                    {
                        LogDebug($"Roket Footer hatası! Beklenen: 0x{ROCKET_FOOTER[i]:X2}, Gelen: 0x{packet[packet.Length - ROCKET_FOOTER.Length + i]:X2}");
                        return null;
                    }
                }

                int offset = 4; // Header'dan sonra

                var data = new RocketTelemetryData
                {
                    // Arduino paket yapısına göre sıralama
                    PacketCounter = packet[offset++],
                    RocketAltitude = BitConverter.ToSingle(packet, offset), // altitude
                    RocketGpsAltitude = BitConverter.ToSingle(packet, offset + 4), // gpsAlt
                    RocketLatitude = BitConverter.ToSingle(packet, offset + 8), // lat
                    RocketLongitude = BitConverter.ToSingle(packet, offset + 12), // lon
                    GyroX = BitConverter.ToSingle(packet, offset + 16), // gx
                    GyroY = BitConverter.ToSingle(packet, offset + 20), // gy
                    GyroZ = BitConverter.ToSingle(packet, offset + 24), // gz
                    AccelX = BitConverter.ToSingle(packet, offset + 28), // ax
                    AccelY = BitConverter.ToSingle(packet, offset + 32), // ay
                    AccelZ = BitConverter.ToSingle(packet, offset + 36), // az
                    Angle = BitConverter.ToSingle(packet, offset + 40), // pitch
                    RocketTemperature = BitConverter.ToSingle(packet, offset + 44), // temp
                    RocketPressure = BitConverter.ToSingle(packet, offset + 48), // press
                    RocketSpeed = BitConverter.ToSingle(packet, offset + 52), // speed
                    DpDt     = BitConverter.ToSingle(packet, offset + 56), // dpdt
                };

                offset += 60; // 15 float * 4 byte = 60 byte
                data.Status = packet[offset++];
                data.CRC = packet[offset++];
                data.TeamID = packet[offset++];
                // offset şu an 67 - footer 68-69 pozisyonunda

                // CRC kontrolü (footer hariç)
                byte calculatedCRC = 0;
                for (int i = 4; i < packet.Length - ROCKET_FOOTER.Length - 2; i++) // Header hariç, footer ve CRC hariç
                {
                    calculatedCRC ^= packet[i];
                }

                if (calculatedCRC != data.CRC)
                {
                    LogDebug($"Roket CRC hatası! Hesaplanan: 0x{calculatedCRC:X2}, Gelen: 0x{data.CRC:X2}");
                }
                
                return data;
            }
            catch (Exception ex)
            {
                LogDebug($"Rocket telemetry parse hatası: {ex.Message}");
                return null;
            }
        }

        private PayloadTelemetryData? ParsePayloadData(byte[] packet)
        {
            LogDebug($"ParsePayloadData çağrıldı, paket boyutu: {packet.Length}");
            try
            {
                if (packet.Length != PAYLOAD_PACKET_SIZE) return null;

                // Footer kontrolü
                if (packet[PAYLOAD_PACKET_SIZE - 2] != PAYLOAD_FOOTER[0] || packet[PAYLOAD_PACKET_SIZE - 1] != PAYLOAD_FOOTER[1])
                {
                    LogDebug("Payload Footer hatası!");
                    return null;
                }

                // Checksum doğrulaması
                byte receivedChecksum = packet[PAYLOAD_PACKET_SIZE - 3];
                byte calculatedChecksum = 0;
                // Checksum, header ve footer hariç payload verisi üzerinden hesaplanır
                for (int i = PAYLOAD_HEADER.Length; i < PAYLOAD_PACKET_SIZE - 3; i++)
                {
                    calculatedChecksum += packet[i]; // Basit toplama checksum
                }

                if (calculatedChecksum != receivedChecksum)
                {
                    LogDebug($"Payload Checksum hatası! Hesaplanan: {calculatedChecksum}, Gelen: {receivedChecksum}");
                    return null;
                }

                int offset = PAYLOAD_HEADER.Length; // 4
                var data = new PayloadTelemetryData
                {
                    PacketCounter = packet[offset],
                    Altitude = BitConverter.ToSingle(packet, offset + 1),
                    GpsAltitude = BitConverter.ToSingle(packet, offset + 5),
                    Latitude = BitConverter.ToSingle(packet, offset + 9),
                    Longitude = BitConverter.ToSingle(packet, offset + 13),
                    Temperature = BitConverter.ToSingle(packet, offset + 17),
                    Humidity = BitConverter.ToSingle(packet, offset + 21),
                    Pressure = BitConverter.ToSingle(packet, offset + 25),
                    PayloadSpeed = BitConverter.ToSingle(packet, offset + 29),
                    Checksum = receivedChecksum
                };

                return data;
            }
            catch (Exception ex)
            {
                LogDebug($"Payload telemetry parse hatası: {ex.Message}");
                return null;
            }
        }

        private void UpdateCharts(RocketTelemetryData data)
        {
            try
            {
                if (ViewModel == null) return;

                ViewModel.AddRocketAltitudeValue(data.RocketAltitude);
                ViewModel.addRocketAccelXValue(data.AccelX);
                ViewModel.addRocketAccelYValue(data.AccelY);
                ViewModel.addRocketAccelZValue(data.AccelZ);
                ViewModel.addRocketSpeedValue(data.RocketSpeed);
                ViewModel.addRocketTempValue(data.RocketTemperature);
                ViewModel.addRocketPressureValue(data.RocketPressure);

                ViewModel.UpdateStatus($"Roket verisi: {DateTime.Now:HH:mm:ss} - Paket: #{data.PacketCounter}");

                LogDebug($"Chart güncellendi - İrtifa: {data.RocketAltitude:F2}m, Hız: {data.RocketSpeed:F2}m/s");
            }
            catch (Exception ex)
            {
                LogDebug($"Chart güncelleme hatası: {ex.Message}");
            }
        }

        private void UpdateCharts(PayloadTelemetryData data)
        {
            try
            {
                if (ViewModel == null) return;

                ViewModel.addPayloadAltitudeValue(data.Altitude);
                ViewModel.addPayloadTempValue(data.Temperature);
                ViewModel.addPayloadPressureValue(data.Pressure);
                ViewModel.addPayloadHumidityValue(data.Humidity);
                ViewModel.addPayloadSpeedValue(data.PayloadSpeed);

                ViewModel.UpdateStatus($"Payload verisi: {DateTime.Now:HH:mm:ss} - Paket: #{data.PacketCounter}");
                LogDebug($"Payload Chart güncellendi - İrtifa: {data.Altitude:F2}m, Sıcaklık: {data.Temperature:F2}°C");
            }
            catch (Exception ex)
            {
                LogDebug($"Payload Chart güncelleme hatası: {ex.Message}");
            }
        }

        public async Task WriteAsync(byte[] data)
        {
            if (_inputPort == null || !_inputPort.IsOpen)
            {
                throw new InvalidOperationException("Port açık değil");
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

        public async Task StopReadingAsync()
        {
            try
            {
                if (_inputPort?.IsOpen == true)
                {
                    _inputPort.DataReceived -= SerialPort_DataReceived;
                }

                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                    if (_processingTask != null)
                    {
                        await _processingTask;
                    }
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                if (_inputPort?.IsOpen == true)
                {
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
            // Bu method interface uyumluluğu için gerekli ama bu basit implementasyonda kullanılmıyor
        }

        private async Task DisposePortAsync()
        {
            if (_inputPort != null)
            {
                try
                {
                    if (_inputPort.IsOpen)
                    {
                        await Task.Run(() => _inputPort.Close());
                    }
                    _inputPort.Dispose();
                    _inputPort = null;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Port dispose hatası");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopReadingAsync();
                await DisposePortAsync();
                
                lock (_bufferLock)
                {
                    _buffer.Clear();
                }

                _logger?.LogInformation("SerialPortService dispose edildi");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Dispose hatası");
            }
        }
        
        // Performance Optimized Debug Methods - sadece DEBUG modunda çalışır
        [Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
        
        [Conditional("DEBUG")]
        private void LogDebugData(byte[] tempBuffer, int bytesRead)
        {
            var hexString = BitConverter.ToString(tempBuffer, 0, bytesRead).Replace("-", " ");
            OnDataReceived?.Invoke($"[{bytesRead} byte] {hexString}");
        }
        
       
    }
}