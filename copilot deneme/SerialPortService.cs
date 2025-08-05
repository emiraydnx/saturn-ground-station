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
                    // Queue'ya ekle, async işlem için
                    _dataQueue.Enqueue(tempBuffer.Take(bytesRead).ToArray());
                    
                    // String data event'i
                    string dataAsString = System.Text.Encoding.UTF8.GetString(tempBuffer, 0, bytesRead);
                    OnDataReceived?.Invoke(dataAsString);
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
                    ProcessHYIPackets();
                    ProcessPayloadTelemetryPackets();
                    ProcessRocketTelemetryPackets();
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
                    if (_binaryBuffer.Count > header.Length)
                        _binaryBuffer.RemoveAt(0);
                    else
                        break;
                    continue;
                }

                if (headerIndex > 0)
                {
                    _logger?.LogDebug("{PacketType} Header {Index} pozisyonunda bulundu", packetType, headerIndex);
                    _binaryBuffer.RemoveRange(0, headerIndex);
                    continue;
                }

                if (_binaryBuffer.Count < packetSize)
                    break;

                byte[] packet = _binaryBuffer.GetRange(0, packetSize).ToArray();
                var telemetryData = parser(packet);

                if (telemetryData != null)
                {
                    Dispatcher?.TryEnqueue(() => onDataReceived(telemetryData));
                }

                _binaryBuffer.RemoveRange(0, packetSize);
            }
        }

        public async Task StopReadingAsync()
        {
            try
            {
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

        // Rest of the existing methods with improved error handling...
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

        // Improved packet processing methods...
        private void ProcessHYIPackets()
        {
            ProcessPackets(HYI_HEADER, HYI_PACKET_SIZE, ParseHYIData, 
                data => {
                    OnHYIPacketReceived?.Invoke(data);
                    
                    // HYI paketini output port'a forward et
                    if (IsOutputPortOpen())
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                byte[] packet = new byte[HYI_PACKET_SIZE];
                                // HYI paketini yeniden oluştur
                                await WriteToOutputPortAsync(packet);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError(ex, "HYI paket forward hatası");
                            }
                        });
                    }
                }, "HYI");
        }

        private void ProcessPayloadTelemetryPackets()
        {
            ProcessPackets(PAYLOAD_HEADER, PAYLOAD_PACKET_SIZE, ParsePayloadData,
                data => {
                    OnPayloadDataUpdated?.Invoke(data);
                    CheckAndFireTelemetryUpdate(null, data);
                }, "Payload");
        }

        private void ProcessRocketTelemetryPackets()
        {
            ProcessPackets(ROCKET_HEADER, ROCKET_PACKET_SIZE, ParseRocketData,
                data => {
                    OnRocketDataUpdated?.Invoke(data);
                    CheckAndFireTelemetryUpdate(data, null);
                    
                    // Rotation data event'ini fırlatmak için
                    OnRotationDataReceived?.Invoke(data.GyroX, data.GyroY, data.GyroZ);
                }, "Rocket");
        }

        private RocketTelemetryData? _lastRocketData;
        private PayloadTelemetryData? _lastPayloadData;

        private void CheckAndFireTelemetryUpdate(RocketTelemetryData? rocketData, PayloadTelemetryData? payloadData)
        {
            if (rocketData != null) _lastRocketData = rocketData;
            if (payloadData != null) _lastPayloadData = payloadData;

            if (_lastRocketData != null && _lastPayloadData != null)
            {
                OnTelemetryDataUpdated?.Invoke(_lastRocketData, _lastPayloadData);
                UpdateCharts(_lastRocketData, _lastPayloadData);
            }
        }

        private void UpdateCharts(RocketTelemetryData rocketTelemetry, PayloadTelemetryData payloadTelemetry)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                        return;

                    UpdateViewModelData(rocketTelemetry.RocketAltitude, payloadTelemetry.PayloadAltitude,
                        rocketTelemetry.RocketSpeed, payloadTelemetry.PayloadSpeed,
                        rocketTelemetry.RocketTemperature, payloadTelemetry.PayloadTemperature,
                        rocketTelemetry.RocketPressure, payloadTelemetry.PayloadPressure,
                        payloadTelemetry.PayloadHumidity,
                        rocketTelemetry.AccelX, rocketTelemetry.AccelY, rocketTelemetry.AccelZ);

                    ViewModel.UpdateStatus($"Serial verisi: {DateTime.Now:HH:mm:ss} - Paket: {rocketTelemetry.PacketCounter}");
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

        // Existing parsing methods remain the same...
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

        private static RocketTelemetryData? ParseRocketData(byte[] packet)
        {
            try
            {
                if (!IsValidPacket(packet, ROCKET_HEADER))
                    return null;

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
                byte calculatedCRC = CalculateSimpleCRC(packet, 4, 57);
                if (calculatedCRC != packet[62])
                {
                    Debug.WriteLine("Rocket telemetry CRC hatası!");
                }

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

        // Data classes remain the same...
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
    }
}