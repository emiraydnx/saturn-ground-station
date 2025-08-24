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

        // HYI Paketi için sabitler (78 byte)
        private const int HYI_PACKET_SIZE = 78;
        private static readonly byte[] HYI_HEADER = { 0xFF, 0xFF, 0x54, 0x52 };
        private static readonly byte[] HYI_FOOTER = { 0x0D, 0x0A };

        // HYI veri tutma ve birleştirme
        private HYITelemetryData _currentHyiData;
        private RocketTelemetryData? _lastRocketData;
        private PayloadTelemetryData? _lastPayloadData;
        private byte _hyiPacketCounter = 0;

        // Output Port Service referansı
        private SerialPortService? _outputPortService;

        public SerialPortService(ILogger<SerialPortService>? logger = null)
        {
            _logger = logger;
            // HYI data'yı başlat
            _currentHyiData = new HYITelemetryData();
        }

        #region Properties
        public ChartViewModel? ViewModel { get; set; }
        public DispatcherQueue? Dispatcher { get; set; }

        // Output Port Service property'si - SettingPage'den set edilecek
        public SerialPortService? OutputPortService 
        { 
            get => _outputPortService; 
            set => _outputPortService = value; 
        }
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
            // Kademe GPS verileri - her zaman 0.0f döndürür
            public float KademeGPSİrtifa { get; set; } = 0.0f;
            public float KademeEnlem { get; set; } = 0.0f;
            public float KademeBoylam { get; set; } = 0.0f;
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

            // Port zaten açıksa, kullanıcıyı bilgilendir ve işlemi sonlandır.
            if (_inputPort.IsOpen)
            {
                var message = $"Port zaten açık: {_inputPort.PortName}";
                _logger?.LogInformation(message);
                OnError?.Invoke(message); // UI'da göstermek için event'i tetikle
                return; // Metoddan çık
            }

            try
            {
                // Portu aç
                await Task.Run(() => _inputPort.Open());

                _cancellationTokenSource = new CancellationTokenSource();
                _processingTask = Task.Run(() => ProcessBufferLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

                _inputPort.DataReceived -= SerialPort_DataReceived;
                _inputPort.DataReceived += SerialPort_DataReceived;

                _logger?.LogInformation("SerialPort okuma başlatıldı - Port açık: {IsOpen}", _inputPort.IsOpen);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SerialPort okuma başlatma hatası");
                OnError?.Invoke($"{ex.Message}");
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
                            _lastRocketData = telemetryData;
                            
                            // HYI verilerini Roket verisinden güncelle
                            UpdateHYIDataFromRocket(telemetryData);

                            Dispatcher?.TryEnqueue(() =>
                            {
                                OnRocketDataUpdated?.Invoke(telemetryData);
                                OnTelemetryDataUpdated?.Invoke(telemetryData);
                                OnRotationDataReceived?.Invoke(telemetryData.GyroX, telemetryData.GyroY, telemetryData.GyroZ);
                              
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
                            _lastPayloadData = payloadData;
                            
                            // HYI verilerini Payload verisinden güncelle
                            UpdateHYIDataFromPayload(payloadData);

                            Dispatcher?.TryEnqueue(() =>
                            {
                                OnPayloadDataUpdated?.Invoke(payloadData);
                                UpdateCharts(payloadData);
                            });

                            // HYI paketini output port'a gönder (sadece payload geldiğinde)
                            Task.Run(async () => await SendHYIPacketAsync());
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

        // HYI verilerini Roket telemetrisinden güncelle
        private void UpdateHYIDataFromRocket(RocketTelemetryData rocketData)
        {
            if (_currentHyiData == null) return;

            _currentHyiData.TeamId = rocketData.TeamID;
            _currentHyiData.Altitude = rocketData.RocketAltitude;
            _currentHyiData.RocketGpsAltitude = rocketData.RocketGpsAltitude;
            _currentHyiData.RocketLatitude = rocketData.RocketLatitude;
            _currentHyiData.RocketLongitude = rocketData.RocketLongitude;
            _currentHyiData.GyroscopeX = rocketData.GyroX;
            _currentHyiData.GyroscopeY = rocketData.GyroY;
            _currentHyiData.GyroscopeZ = rocketData.GyroZ;
            _currentHyiData.AccelerationX = rocketData.AccelX;
            _currentHyiData.AccelerationY = rocketData.AccelY;
            _currentHyiData.AccelerationZ = rocketData.AccelZ;
            _currentHyiData.Angle = rocketData.Angle;
            _currentHyiData.Status = rocketData.Status;

            LogDebug($"HYI Data Roket güncellemesi: İrtifa={rocketData.RocketAltitude:F2}m");
        }

        // HYI verilerini Payload telemetrisinden güncelle
        private void UpdateHYIDataFromPayload(PayloadTelemetryData payloadData)
        {
            if (_currentHyiData == null) return;

            _currentHyiData.PayloadGpsAltitude = payloadData.GpsAltitude;
            _currentHyiData.PayloadLatitude = payloadData.Latitude;
            _currentHyiData.PayloadLongitude = payloadData.Longitude;
            
            // Kademe GPS verileri her zaman 0 kalır - değiştirilmez

            LogDebug($"HYI Data Payload güncellemesi: Payload İrtifa={payloadData.Altitude:F2}m");
        }

        // HYI paketini 78 byte olarak oluştur ve output port'a gönder
        private async Task SendHYIPacketAsync()
        {
            try
            {
                if (_currentHyiData == null || _outputPortService?.IsPortOpen() != true)
                {
                    LogDebug("HYI paketi gönderilemedi: OutputPort kapalı veya HYI data null");
                    return;
                }

                // Paket counter'ı artır
                _currentHyiData.PacketCounter = _hyiPacketCounter++;

                // 78 byte'lık HYI paketini oluştur
                byte[] hyiPacket = CreateHYIPacket(_currentHyiData);

                // Output port'a gönder
                await _outputPortService.WriteAsync(hyiPacket);

                // HYI Event tetikle
                Dispatcher?.TryEnqueue(() =>
                {
                    OnHYIPacketReceived?.Invoke(_currentHyiData);
                });

                LogDebug($"HYI Paketi gönderildi - 78 byte, Paket #{_currentHyiData.PacketCounter}, TeamID: {_currentHyiData.TeamId}");
            }
            catch (Exception ex)
            {
                LogDebug($"HYI paketi gönderme hatası: {ex.Message}");
            }
        }

        // 78 byte HYI paketi oluştur
        private byte[] CreateHYIPacket(HYITelemetryData hyiData)
        {
            byte[] packet = new byte[HYI_PACKET_SIZE];
            int offset = 0;

            // Header (4 byte)
            Array.Copy(HYI_HEADER, 0, packet, offset, HYI_HEADER.Length);
            offset += HYI_HEADER.Length;

            // Team ID (1 byte)
            packet[offset++] = hyiData.TeamId;

            // Packet Counter (1 byte)
            packet[offset++] = hyiData.PacketCounter;

            // Float veriler (18 * 4 = 72 byte) - Kademe GPS dahil
            BitConverter.GetBytes(hyiData.Altitude).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.RocketGpsAltitude).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.RocketLatitude).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.RocketLongitude).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.PayloadGpsAltitude).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.PayloadLatitude).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.PayloadLongitude).CopyTo(packet, offset);
            offset += 4;
            // Kademe GPS verileri (her zaman 0.0f)
            BitConverter.GetBytes(hyiData.KademeGPSİrtifa).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.KademeEnlem).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.KademeBoylam).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.GyroscopeX).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.GyroscopeY).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.GyroscopeZ).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.AccelerationX).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.AccelerationY).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.AccelerationZ).CopyTo(packet, offset);
            offset += 4;
            BitConverter.GetBytes(hyiData.Angle).CopyTo(packet, offset);
            offset += 4;

            // Status (1 byte)
            packet[offset++] = hyiData.Status;

            // CRC hesapla (1 byte) - Header hariç, footer hariç tüm veriler üzerinden
            byte crc = 0;
            for (int i = HYI_HEADER.Length; i < offset; i++)
            {
                crc ^= packet[i];
            }
            packet[offset++] = crc;
            hyiData.CRC = crc;

            // Footer (2 byte)
            Array.Copy(HYI_FOOTER, 0, packet, offset, HYI_FOOTER.Length);

            LogDebug($"HYI Paketi oluşturuldu: {packet.Length} byte, CRC: 0x{crc:X2}");

            return packet;
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