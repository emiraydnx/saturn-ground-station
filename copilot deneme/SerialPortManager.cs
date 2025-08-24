using copilot_deneme.ViewModels;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace copilot_deneme
{
    /// <summary>
    /// Singleton SerialPortService Manager - Tüm sayfalar arasında paylaşılan tek instance
    /// Input ve Output portlarını ayrı ayrı yönetir
    /// </summary>
    public sealed class SerialPortManager
    {
        private static SerialPortManager? _instance;
        private static readonly object _lock = new object();

        private SerialPortService? _serialPortService; // Input port
        private SerialPortService? _outputPortService; // Output port
        
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
        public SerialPortService? OutputPortService => _outputPortService;
        public bool IsConnected => _serialPortService?.IsPortOpen() == true;
        public bool IsOutputConnected => _outputPortService?.IsPortOpen() == true;

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

                LogDebug($"SerialPortManager: Input port bağlandı {portName} @ {baudRate}");
            }
            catch (Exception ex)
            {
                LogDebug($"SerialPortManager input port başlatma hatası: {ex.Message}");
                throw;
            }
        }

        public async Task InitializeOutputAsync(string portName, int baudRate, ChartViewModel viewModel, DispatcherQueue dispatcher)
        {
            try
            {
                if (_outputPortService != null)
                {
                    await _outputPortService.DisposeAsync();
                }

                _outputPortService = new SerialPortService();
                _outputPortService.ViewModel = viewModel;
                _outputPortService.Dispatcher = dispatcher;

                await _outputPortService.InitializeAsync(portName, baudRate);
                await _outputPortService.StartReadingAsync();

                LogDebug($"SerialPortManager: Output port bağlandı {portName} @ {baudRate}");
            }
            catch (Exception ex)
            {
                LogDebug($"SerialPortManager output port başlatma hatası: {ex.Message}");
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
                    LogDebug("SerialPortManager: Input port bağlantısı kesildi");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"SerialPortManager input port kapatma hatası: {ex.Message}");
            }
        }

        public async Task DisconnectOutputAsync()
        {
            try
            {
                if (_outputPortService != null)
                {
                    await _outputPortService.StopReadingAsync();
                    LogDebug("SerialPortManager: Output port bağlantısı kesildi");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"SerialPortManager output port kapatma hatası: {ex.Message}");
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

        public string GetOutputConnectionInfo()
        {
            return _outputPortService?.GetPortInfo() ?? "Output bağlantısı yok";
        }

        // Conditional Debug Logging - RELEASE'de hiç çalışmaz
        [Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}