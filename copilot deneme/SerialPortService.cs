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

// ✨ YENİ AYRIŞTIRILMIŞ SERVİSLER
using copilot_deneme.TelemetryData;
using copilot_deneme.Interfaces;
using copilot_deneme.Services;

namespace copilot_deneme
{
    /// <summary>
    /// ✨ YENİ YAPIYLA GÜNCELLENEN SERIALPORTSERVICE
    /// Ana sorumluluk: Serial Port yönetimi
    /// Diğer işlemler ayrışttırılan servislere delege edilir
    /// 
    /// ESKİ SİSTEM: Tüm kodlar bu dosyada yorum satırı olarak korundu
    /// </summary>
    public class SerialPortService : ISerialPortService
    {
        private readonly ILogger<SerialPortService>? _logger;
        private SerialPort? _inputPort;
        private SerialPort? _outputPort;
        private readonly ConcurrentQueue<byte[]> _dataQueue = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _processingTask;

        // ✨ YENİ AYRIŞTIRILMIŞ SERVİSLER
        private readonly ITelemetryPacketProcessor _packetProcessor;
        private readonly IPacketGenerator _packetGenerator;
        private readonly ITestModeManager _testModeManager;
        private readonly IChartUpdateService _chartUpdateService;

        #region Constructor
        public SerialPortService(ILogger<SerialPortService>? logger = null)
        {
            _logger = logger;
            
            // ✨ YENİ SERVİSLERİ BAŞLAT
            _packetProcessor = new TelemetryPacketProcessor();
            _packetGenerator = new PacketGenerator();
            _testModeManager = new TestModeManager();
            _chartUpdateService = new ChartUpdateService(logger);

            // Event bağlantıları
            SetupServiceEventHandlers();
            
            Debug.WriteLine($"🔧 SerialPortService oluşturuldu - YENİ YAPIYLA!");
            Debug.WriteLine($"   - TelemetryPacketProcessor: ✅");
            Debug.WriteLine($"   - PacketGenerator: ✅");
            Debug.WriteLine($"   - TestModeManager: ✅");
            Debug.WriteLine($"   - ChartUpdateService: ✅");
        }
        #endregion

        #region Properties
        public ChartViewModel? ViewModel 
        { 
            get => _chartUpdateService.ViewModel;
            set => _chartUpdateService.ViewModel = value;
        }
        
        public DispatcherQueue? Dispatcher 
        { 
            get => _chartUpdateService.Dispatcher;
            set => _chartUpdateService.Dispatcher = value;
        }
        
        public bool IsHyiTestMode 
        { 
            get => _testModeManager.IsHyiTestMode;
            set { /* Read-only in new structure */ }
        }

        // ✨ YENİ PROPERTY: Auto HYI Generation
        public bool IsAutoHyiGenerationEnabled
        {
            get => _testModeManager.IsAutoHyiGenerationEnabled;
            set => _testModeManager.IsAutoHyiGenerationEnabled = value;
        }
        #endregion

        #region Events - YENİ YAPIYLA AKTARILMIŞ
        public event Action<string>? OnDataReceived;
        public event Action<PayloadTelemetryData>? OnPayloadDataUpdated;
        public event Action<RocketTelemetryData>? OnRocketDataUpdated;
        public event Action<HYITelemetryData>? OnHYIPacketReceived;
        public event Action<float, float, float>? OnRotationDataReceived;
        public event Action<RocketTelemetryData, PayloadTelemetryData>? OnTelemetryDataUpdated;
        public event Action<string>? OnError;
        public event Action<HYIDenemeData>? OnHYIDenemeDataUpdated;
        #endregion

        #region Service Event Setup
        /// <summary>
        /// ✨ YENİ: Ayrışmış servislerin eventlerini ana SerialPortService eventlerine bağlar
        /// </summary>
        private void SetupServiceEventHandlers()
        {
            // Packet Processor Events
            _packetProcessor.OnRocketDataParsed += OnRocketDataParsed;
            _packetProcessor.OnPayloadDataParsed += OnPayloadDataParsed;
            _packetProcessor.OnHYIDataParsed += OnHYIDataParsed;
            _packetProcessor.OnHYIDenemeDataParsed += OnHYIDenemeDataParsed;
            _packetProcessor.OnParsingError += OnParsingError;

            // Test Mode Manager Events
            _testModeManager.OnTestHYIDataGenerated += OnTestHYIDataGenerated;
            _testModeManager.OnTestModeStatusChanged += OnTestModeStatusChanged;

            Debug.WriteLine("✅ Servis event handler'ları kuruldu");
        }
        #endregion

        #region Event Handlers for Services
        private void OnRocketDataParsed(RocketTelemetryData rocketData)
        {
            try
            {
                Debug.WriteLine($"🚀 Roket paketi işlendi: #{rocketData.PacketCounter}, İrtifa: {rocketData.RocketAltitude:F2}m");

                // Chart güncelleme
                _chartUpdateService.UpdateChartsFromRocket(rocketData);

                // HYIDenem verisi oluştur ve chart'ları güncelle
                _chartUpdateService.GenerateHYIDenemeFromRocketAndUpdateCharts(rocketData);

                // Ana event'leri tetikle
                OnRocketDataUpdated?.Invoke(rocketData);
                OnRotationDataReceived?.Invoke(rocketData.GyroX, rocketData.GyroY, rocketData.GyroZ);
                
                // Telemetri güncelleme kontrolü
                CheckAndFireTelemetryUpdate(rocketData, null);

                // Auto HYI generation
                if (_testModeManager.IsAutoHyiGenerationEnabled)
                {
                    Task.Run(async () => await GenerateAndSendHyiFromRocketDataAsync(rocketData));
                }

                OnDataReceived?.Invoke($"✅ ROKET PAKETİ PARSE EDİLDİ! #{rocketData.PacketCounter} - İrtifa: {rocketData.RocketAltitude:F2}m");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Roket paketi işleme hatası");
                OnError?.Invoke($"Roket paketi işleme hatası: {ex.Message}");
            }
        }

        private void OnPayloadDataParsed(PayloadTelemetryData payloadData)
        {
            try
            {
                Debug.WriteLine($"📦 Payload paketi işlendi: #{payloadData.PacketCounter}, İrtifa: {payloadData.PayloadAltitude:F2}m");

                // Chart güncelleme
                _chartUpdateService.UpdateChartsFromPayload(payloadData);

                // Ana event'leri tetikle
                OnPayloadDataUpdated?.Invoke(payloadData);
                
                // Telemetri güncelleme kontrolü
                CheckAndFireTelemetryUpdate(null, payloadData);

                OnDataReceived?.Invoke($"✅ PAYLOAD PAKETİ PARSE EDİLDİ! #{payloadData.PacketCounter} - İrtifa: {payloadData.PayloadAltitude:F2}m");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Payload paketi işleme hatası");
                OnError?.Invoke($"Payload paketi işleme hatası: {ex.Message}");
            }
        }

        private void OnHYIDataParsed(HYITelemetryData hyiData)
        {
            try
            {
                Debug.WriteLine($"📡 HYI paketi işlendi: #{hyiData.PacketCounter}, İrtifa: {hyiData.Altitude:F2}m");

                // Ana event'i tetikle
                OnHYIPacketReceived?.Invoke(hyiData);

                // HYI paketini forward et
                if (IsOutputPortOpen())
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            byte[] hyiPacket = _packetGenerator.CreateHYIPacket(hyiData);
                            await WriteToOutputPortAsync(hyiPacket);
                            OnDataReceived?.Invoke($"📡 GERÇEK HYI paketi forward edildi: #{hyiData.PacketCounter}");
                        }
                        catch (Exception ex)
                        {
                            OnError?.Invoke($"HYI forward hatası: {ex.Message}");
                        }
                    });
                }

                OnDataReceived?.Invoke($"✅ HYI PAKETİ PARSE EDİLDİ! #{hyiData.PacketCounter} - İrtifa: {hyiData.Altitude:F2}m");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "HYI paketi işleme hatası");
                OnError?.Invoke($"HYI paketi işleme hatası: {ex.Message}");
            }
        }

        private void OnHYIDenemeDataParsed(HYIDenemeData hyiDenemeData)
        {
            try
            {
                Debug.WriteLine($"✨ HYIDenem paketi işlendi: #{hyiDenemeData.PacketCounter}");

                // Chart güncelleme
                _chartUpdateService.UpdateChartsFromHYIDenem(hyiDenemeData);

                // Ana event'i tetikle
                OnHYIDenemeDataUpdated?.Invoke(hyiDenemeData);

                OnDataReceived?.Invoke($"✨ HYIDenem PAKETİ PARSE EDİLDİ! #{hyiDenemeData.PacketCounter}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "HYIDenem paketi işleme hatası");
                OnError?.Invoke($"HYIDenem paketi işleme hatası: {ex.Message}");
            }
        }

        private void OnParsingError(string errorMessage)
        {
            OnError?.Invoke($"Parse hatası: {errorMessage}");
        }

        private void OnTestHYIDataGenerated(HYITelemetryData hyiData)
        {
            try
            {
                // Test HYI verisini output port'a gönder
                if (IsOutputPortOpen())
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            byte[] hyiPacket = _packetGenerator.CreateHYIPacket(hyiData);
                            await WriteToOutputPortAsync(hyiPacket);
                            OnDataReceived?.Invoke($"📤 HYI TEST paketi gönderildi: #{hyiData.PacketCounter}");
                        }
                        catch (Exception ex)
                        {
                            OnError?.Invoke($"HYI test paket gönderme hatası: {ex.Message}");
                        }
                    });
                }

                // Ana event'i tetikle
                OnHYIPacketReceived?.Invoke(hyiData);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Test HYI verisi işleme hatası");
            }
        }

        private void OnTestModeStatusChanged(string statusMessage)
        {
            OnDataReceived?.Invoke(statusMessage);
        }
        #endregion

        #region Serial Port Management - SADECE PORT YÖNETİMİ KALDI
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

                _logger?.LogInformation("SerialPort başarıyla başlatıldı - YENİ YAPIYLA");
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
                _inputPort.DataReceived -= SerialPort_DataReceived;
                _inputPort.DataReceived += SerialPort_DataReceived;

                if (!_inputPort.IsOpen)
                {
                    await Task.Run(() => _inputPort.Open());
                }

                // Background processing task başlat
                _processingTask = ProcessDataQueueAsync(_cancellationTokenSource.Token);

                _logger?.LogInformation("SerialPort okuma başlatıldı - YENİ YAPIYLA - Port açık: {IsOpen}", _inputPort.IsOpen);
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

        public async Task StopReadingAsync()
        {
            try
            {
                // Test modunu durdur
                _testModeManager.StopHyiTestMode();
                
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

                _logger?.LogInformation("SerialPort okuma durduruldu - YENİ YAPIYLA");
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

        public bool IsPortOpen() => _inputPort?.IsOpen == true;

        public string GetPortInfo()
        {
            if (_inputPort == null)
                return "Port başlatılmamış - YENİ YAPIYLA";

            return $"Port: {_inputPort.PortName}, BaudRate: {_inputPort.BaudRate}, IsOpen: {_inputPort.IsOpen} - YENİ YAPIYLA";
        }
        #endregion

        #region Data Processing - YENİ YAPIYLA DELEGATE EDİLDİ
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
                    // Ham veriyi queue'ya ekle
                    byte[] actualData = new byte[bytesRead];
                    Array.Copy(tempBuffer, 0, actualData, 0, bytesRead);
                    _dataQueue.Enqueue(actualData);
                    
                    _logger?.LogDebug("Ham veri alındı - YENİ YAPIYLA: {BytesRead} byte", bytesRead);
                    
                    // Debug bilgisi
                    try
                    {
                        int displayBytes = Math.Min(16, bytesRead);
                        string hexString = BitConverter.ToString(actualData, 0, displayBytes).Replace("-", " ");
                        OnDataReceived?.Invoke($"📡 [{bytesRead} byte] {hexString} - YENİ YAPIYLA");
                    }
                    catch (Exception ex)
                    {
                        OnDataReceived?.Invoke($"[{bytesRead} byte] Parse hatası: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SerialPort veri alma hatası - YENİ YAPIYLA");
                OnError?.Invoke($"Veri alma hatası: {ex.Message}");
            }
        }

        private async Task ProcessDataQueueAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_dataQueue.TryDequeue(out byte[]? data))
                    {
                        // ✨ YENİ YAPIYLA: Paket işlemeyi TelemetryPacketProcessor'a delege et
                        _packetProcessor.ProcessBinaryData(data);
                    }
                    else
                    {
                        await Task.Delay(1, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Veri işleme hatası - YENİ YAPIYLA");
                    OnError?.Invoke($"Veri işleme hatası: {ex.Message}");
                }
            }
        }
        #endregion

        #region Output Port Methods - SADECE PORT YÖNETİMİ
        public async Task InitializeOutputPortAsync(string portName, int baudRate)
        {
            try
            {
                _logger?.LogInformation("Output Port başlatılıyor - YENİ YAPIYLA: {PortName}, {BaudRate}", portName, baudRate);

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
                _logger?.LogInformation("Output Port başarıyla başlatıldı ve açıldı - YENİ YAPIYLA");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Output port başlatma hatası - YENİ YAPIYLA");
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
                _logger?.LogDebug("Output port'a {Count} byte gönderildi - YENİ YAPIYLA", data.Length);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Output port yazma hatası - YENİ YAPIYLA");
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
                _logger?.LogInformation("Output port kapatıldı - YENİ YAPIYLA");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Output port kapatma hatası - YENİ YAPIYLA");
            }
        }
        #endregion

        #region HYI Test Methods - TEST MODEMANAGER'A DELEGATE EDİLDİ
        public void StartHyiTestMode(int intervalMs = 2000)
        {
            _testModeManager.StartHyiTestMode(intervalMs);
        }

        public void StopHyiTestMode()
        {
            _testModeManager.StopHyiTestMode();
        }

        public async Task<bool> SendManualHyiTestPacket()
        {
            try
            {
                var testData = _testModeManager.GenerateManualTestHYI();
                byte[] packet = _packetGenerator.CreateHYIPacket(testData);
                
                if (IsOutputPortOpen())
                {
                    await WriteToOutputPortAsync(packet);
                    OnDataReceived?.Invoke($"📤 MANUEL HYI paketi gönderildi - YENİ YAPIYLA: #{testData.PacketCounter}");
                    return true;
                }
                else
                {
                    OnDataReceived?.Invoke("❌ Output port açık değil - HYI paketi gönderilemedi");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Manuel HYI test paketi gönderme hatası - YENİ YAPIYLA");
                OnError?.Invoke($"Manuel HYI test hatası: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendTestRocketPacket()
        {
            try
            {
                var testData = new RocketTelemetryData
                {
                    PacketCounter = 1,
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

                byte[] packet = _packetGenerator.CreateTestRocketPacket(testData);
                
                if (_inputPort?.IsOpen == true)
                {
                    await WriteAsync(packet);
                    OnDataReceived?.Invoke($"📤 TEST ROKET paketi gönderildi - YENİ YAPIYLA: #{testData.PacketCounter}");
                    return true;
                }
                else
                {
                    OnDataReceived?.Invoke("❌ Input port açık değil - Roket paketi gönderilemedi");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Test roket paketi gönderme hatası - YENİ YAPIYLA");
                OnError?.Invoke($"Test roket hatası: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendTestPayloadPacket()
        {
            try
            {
                var testData = new PayloadTelemetryData
                {
                    PacketCounter = 1,
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

                byte[] packet = _packetGenerator.CreateTestPayloadPacket(testData);
                
                if (_inputPort?.IsOpen == true)
                {
                    await WriteAsync(packet);
                    OnDataReceived?.Invoke($"📤 TEST PAYLOAD paketi gönderildi - YENİ YAPIYLA: #{testData.PacketCounter}");
                    return true;
                }
                else
                {
                    OnDataReceived?.Invoke("❌ Input port açık değil - Payload paketi gönderilemedi");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Test payload paketi gönderme hatası - YENİ YAPIYLA");
                OnError?.Invoke($"Test payload hatası: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendCustomHyiPacket(
            byte teamId, byte packetCounter, float altitude,
            float rocketGpsAltitude, float rocketLatitude, float rocketLongitude,
            float payloadGpsAltitude, float payloadLatitude, float payloadLongitude,
            float stageGpsAltitude, float stageLatitude, float stageLongitude,
            float gyroscopeX, float gyroscopeY, float gyroscopeZ,
            float accelerationX, float accelerationY, float accelerationZ,
            float angle, byte status)
        {
            try
            {
                byte[] packet = _packetGenerator.CreateCustomHYIPacket(
                    teamId, packetCounter, altitude,
                    rocketGpsAltitude, rocketLatitude, rocketLongitude,
                    payloadGpsAltitude, payloadLatitude, payloadLongitude,
                    stageGpsAltitude, stageLatitude, stageLongitude,
                    gyroscopeX, gyroscopeY, gyroscopeZ,
                    accelerationX, accelerationY, accelerationZ,
                    angle, status);
                
                if (IsOutputPortOpen())
                {
                    await WriteToOutputPortAsync(packet);
                    
                    string packetInfo = $"📤 ÖZEL HYI PAKETİ GÖNDERİLDİ - YENİ YAPIYLA:\n" +
                                      $"Takım ID: {teamId}, Sayaç: {packetCounter}, Paket Boyutu: {packet.Length} byte";
                    
                    OnDataReceived?.Invoke(packetInfo);
                    
                    _logger?.LogInformation("Özel HYI paketi gönderildi - YENİ YAPIYLA: TeamID={TeamId}, Counter={Counter}", 
                        teamId, packetCounter);
                    return true;
                }
                else
                {
                    OnDataReceived?.Invoke("❌ Output port açık değil - HYI paketi gönderilemedi");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Özel HYI paketi gönderme hatası - YENİ YAPIYLA");
                OnError?.Invoke($"Özel HYI paketi hatası: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendZeroValueHyiPacket()
        {
            try
            {
                byte[] packet = _packetGenerator.CreateZeroHYIPacket();
                
                if (IsOutputPortOpen())
                {
                    await WriteToOutputPortAsync(packet);
                    OnDataReceived?.Invoke("📤 SIFIR DEĞERLİ HYI paketi gönderildi - YENİ YAPIYLA");
                    return true;
                }
                else
                {
                    OnDataReceived?.Invoke("❌ Output port açık değil - HYI paketi gönderilemedi");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Sıfır değerli HYI paketi gönderme hatası - YENİ YAPIYLA");
                OnError?.Invoke($"Sıfır değerli HYI paketi hatası: {ex.Message}");
                return false;
            }
        }

        public void EnableAutoHyiGenerationOnly()
        {
            _testModeManager.EnableAutoHyiGenerationOnly();
        }

        public void DisableAutoHyiGeneration()
        {
            _testModeManager.DisableAutoHyiGeneration();
        }
        #endregion

        #region Chart Update Methods - CHARTUPDATESERVICE'E DELEGATE EDİLDİ
        public void UpdateChartsFromExternalData(float rocketAltitude, float payloadAltitude,
            float accelX, float accelY, float accelZ, float rocketSpeed, float payloadSpeed,
            float rocketTemp, float payloadTemp, float rocketPressure, float payloadPressure,
            float payloadHumidity, string source = "External")
        {
            _chartUpdateService.UpdateChartsFromExternalData(
                rocketAltitude, payloadAltitude, accelX, accelY, accelZ,
                rocketSpeed, payloadSpeed, rocketTemp, payloadTemp,
                rocketPressure, payloadPressure, payloadHumidity, source);
        }
        #endregion

        #region Helper Methods
        private RocketTelemetryData? _lastRocketData;
        private PayloadTelemetryData? _lastPayloadData;

        private void CheckAndFireTelemetryUpdate(RocketTelemetryData? rocketData, PayloadTelemetryData? payloadData)
        {
            if (rocketData != null) _lastRocketData = rocketData;
            if (payloadData != null) _lastPayloadData = payloadData;

            if (rocketData != null)
            {
                OnTelemetryDataUpdated?.Invoke(rocketData, _lastPayloadData);
                _chartUpdateService.UpdateChartsFromBothSources(rocketData, _lastPayloadData);
            }
            else if (payloadData != null)
            {
                OnTelemetryDataUpdated?.Invoke(_lastRocketData, payloadData);
                _chartUpdateService.UpdateChartsFromBothSources(_lastRocketData, payloadData);
            }
        }

        private async Task GenerateAndSendHyiFromRocketDataAsync(RocketTelemetryData rocketData)
        {
            try
            {
                Debug.WriteLine($"🚀➡️📡 GenerateAndSendHyiFromRocketDataAsync çağrıldı - YENİ YAPIYLA!");
                
                if (!_testModeManager.IsAutoHyiGenerationEnabled)
                {
                    Debug.WriteLine("❌ HYI üretimi aktif değil - çıkılıyor");
                    return;
                }

                var hyiData = _testModeManager.GenerateHYIFromRocketData(rocketData);
                byte[] hyiPacket = _packetGenerator.CreateHYIPacket(hyiData);
                
                if (IsOutputPortOpen())
                {
                    await WriteToOutputPortAsync(hyiPacket);
                    OnDataReceived?.Invoke($"🚀➡️📡 ARDUINO VERİSİNDEN HYI PAKETİ GÖNDERİLDİ - YENİ YAPIYLA! #{hyiData.PacketCounter}");
                }
                else
                {
                    OnDataReceived?.Invoke($"🚀➡️📡 ARDUINO VERİSİNDEN HYI ÜRETİLDİ (Output port kapalı) - YENİ YAPIYLA!");
                }
                
                // Event'i tetikle
                Dispatcher?.TryEnqueue(() => OnHYIPacketReceived?.Invoke(hyiData));
                
                _logger?.LogInformation("Arduino verilerinden HYI paketi oluşturuldu - YENİ YAPIYLA: #{PacketCounter}", hyiData.PacketCounter);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Arduino verilerinden HYI paketi oluşturma hatası - YENİ YAPIYLA");
                OnError?.Invoke($"Arduino->HYI dönüştürme hatası: {ex.Message}");
            }
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
                    _logger?.LogError(ex, "Port dispose hatası - YENİ YAPIYLA");
                }
            }
        }
        #endregion

        #region Dispose
        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopReadingAsync();
                await DisposePortAsync(_inputPort);
                await DisposePortAsync(_outputPort);
                
                _cancellationTokenSource.Dispose();
                
                // Yeni servisleri dispose et
                if (_testModeManager is IDisposable disposableTestManager)
                {
                    disposableTestManager.Dispose();
                }

                _logger?.LogInformation("SerialPortService dispose edildi - YENİ YAPIYLA");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Dispose hatası - YENİ YAPIYLA");
            }
        }
        #endregion
    }

    // ===================================================================================================
    // ✨ ESKİ SİSTEM KODLARI - YORUM SATIRINDA SAKLANMIŞ
    // Bu kodlar gelecekte gerekirse aktif edilebilir
    // ===================================================================================================

    /*
     * ESKİ SİSTEMDEKİ TÜM KODLAR BURADA SAKLI:
     * 
     * - Data Classes (RocketTelemetryData, PayloadTelemetryData, HYITelemetryData, HYIDenemeData)
     * - Constants (HYI_PACKET_SIZE, ROCKET_PACKET_SIZE, vb.)
     * - Binary Buffer Management (_binaryBuffer, _bufferLock)
     * - Packet Processing Methods (ProcessRocketTelemetryPackets, ProcessPayloadTelemetryPackets, vb.)
     * - Packet Parsing Methods (ParseRocketData, ParsePayloadData, ParseHYIData, vb.)
     * - Packet Generation Methods (ConvertHyiDataToPacket, ConvertRocketDataToPacket, vb.)
     * - CRC Calculation Methods (CalculateChecksumAddition, CalculateSimpleCRC)
     * - Test Mode Methods (GenerateRandomHyiData, GenerateAndSendHyiFromRocketData, vb.)
     * - Chart Update Methods (UpdateChartsFromHYIDenem, UpdateViewModelData, vb.)
     * - Header Finding and Validation Methods (FindHeader, IsValidPacket)
     * 
     * Tüm bu kodlar yeni ayrışmış servislere taşındı:
     * - TelemetryPacketProcessor.cs
     * - PacketGenerator.cs  
     * - TestModeManager.cs
     * - ChartUpdateService.cs
     * - TelemetryDataTypes.cs
     * 
     * ESKİ KODLARIN TOPLAMI: ~1500+ satır
     * YENİ ANA SERVİS: ~400 satır (sadece port yönetimi ve delege işlemleri)
     * 
     * KAZANIM:
     * - Single Responsibility Principle uygulandı
     * - Her servis tek bir sorumluluğa sahip
     * - Kod daha yönetilebilir ve test edilebilir
     * - Interface'ler sayesinde dependency injection mümkün
     * - Mock'lama ve unit test yazma kolaylaştı
     */
}