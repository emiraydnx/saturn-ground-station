using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace copilot_deneme.Services
{
    /// <summary>
    /// Global port durumu yöneticisi
    /// Tüm uygulamada port baðlantý durumlarýný takip eder
    /// Sayfalara geçiþ yapýldýðýnda port durumlarýný korur
    /// </summary>
    public static class GlobalPortManager
    {
        private static readonly Dictionary<string, PortConnectionInfo> _portConnections = new();
        private static readonly object _lock = new object();

        #region Events
        /// <summary>
        /// Port durumu deðiþtiðinde tetiklenir
        /// </summary>
        public static event Action<string, PortConnectionInfo>? OnPortStatusChanged;
        #endregion

        #region Port Connection Info
        /// <summary>
        /// Port baðlantý bilgilerini tutar
        /// </summary>
        public class PortConnectionInfo
        {
            public string PortName { get; set; } = string.Empty;
            public int BaudRate { get; set; }
            public bool IsConnected { get; set; }
            public DateTime ConnectedAt { get; set; }
            public PortType Type { get; set; }
            public string StatusText { get; set; } = string.Empty;
            public SerialPortService? ServiceInstance { get; set; }
            public System.IO.Ports.SerialPort? SerialPortInstance { get; set; }
        }

        /// <summary>
        /// Port türleri
        /// </summary>
        public enum PortType
        {
            Input,
            Output,
            Arduino
        }
        #endregion

        #region Port Registration Methods
        /// <summary>
        /// Input port baðlantýsýný kaydet
        /// </summary>
        public static void RegisterInputPort(string portName, int baudRate, SerialPortService serviceInstance)
        {
            lock (_lock)
            {
                var connectionInfo = new PortConnectionInfo
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    IsConnected = true,
                    ConnectedAt = DateTime.Now,
                    Type = PortType.Input,
                    StatusText = $"Baðlandý: {portName} ({baudRate})",
                    ServiceInstance = serviceInstance
                };

                _portConnections["Input"] = connectionInfo;
                
                System.Diagnostics.Debug.WriteLine($"?? GlobalPortManager: Input port kaydedildi - {portName} @ {baudRate}");
                OnPortStatusChanged?.Invoke("Input", connectionInfo);
            }
        }

        /// <summary>
        /// Output port baðlantýsýný kaydet
        /// </summary>
        public static void RegisterOutputPort(string portName, int baudRate, SerialPortService serviceInstance)
        {
            lock (_lock)
            {
                var connectionInfo = new PortConnectionInfo
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    IsConnected = true,
                    ConnectedAt = DateTime.Now,
                    Type = PortType.Output,
                    StatusText = $"HYI Output Aktif: {portName} ({baudRate})",
                    ServiceInstance = serviceInstance
                };

                _portConnections["Output"] = connectionInfo;
                
                System.Diagnostics.Debug.WriteLine($"?? GlobalPortManager: Output port kaydedildi - {portName} @ {baudRate}");
                OnPortStatusChanged?.Invoke("Output", connectionInfo);
            }
        }

        /// <summary>
        /// Arduino port baðlantýsýný kaydet
        /// </summary>
        public static void RegisterArduinoPort(string portName, int baudRate, System.IO.Ports.SerialPort serialPortInstance)
        {
            lock (_lock)
            {
                var connectionInfo = new PortConnectionInfo
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    IsConnected = true,
                    ConnectedAt = DateTime.Now,
                    Type = PortType.Arduino,
                    StatusText = $"Baðlý: {portName}",
                    SerialPortInstance = serialPortInstance
                };

                _portConnections["Arduino"] = connectionInfo;
                
                System.Diagnostics.Debug.WriteLine($"?? GlobalPortManager: Arduino port kaydedildi - {portName} @ {baudRate}");
                OnPortStatusChanged?.Invoke("Arduino", connectionInfo);
            }
        }
        #endregion

        #region Port Unregistration Methods
        /// <summary>
        /// Input port baðlantýsýný kaldýr
        /// </summary>
        public static void UnregisterInputPort()
        {
            lock (_lock)
            {
                if (_portConnections.ContainsKey("Input"))
                {
                    var info = _portConnections["Input"];
                    info.IsConnected = false;
                    info.StatusText = "Giriþ Portu Kapalý";
                    
                    System.Diagnostics.Debug.WriteLine($"?? GlobalPortManager: Input port kaydý kaldýrýldý");
                    OnPortStatusChanged?.Invoke("Input", info);
                    
                    _portConnections.Remove("Input");
                }
            }
        }

        /// <summary>
        /// Output port baðlantýsýný kaldýr
        /// </summary>
        public static void UnregisterOutputPort()
        {
            lock (_lock)
            {
                if (_portConnections.ContainsKey("Output"))
                {
                    var info = _portConnections["Output"];
                    info.IsConnected = false;
                    info.StatusText = "Çýkýþ Portu Kapalý";
                    
                    System.Diagnostics.Debug.WriteLine($"?? GlobalPortManager: Output port kaydý kaldýrýldý");
                    OnPortStatusChanged?.Invoke("Output", info);
                    
                    _portConnections.Remove("Output");
                }
            }
        }

        /// <summary>
        /// Arduino port baðlantýsýný kaldýr
        /// </summary>
        public static void UnregisterArduinoPort()
        {
            lock (_lock)
            {
                if (_portConnections.ContainsKey("Arduino"))
                {
                    var info = _portConnections["Arduino"];
                    info.IsConnected = false;
                    info.StatusText = "Baðlý Deðil";
                    
                    System.Diagnostics.Debug.WriteLine($"?? GlobalPortManager: Arduino port kaydý kaldýrýldý");
                    OnPortStatusChanged?.Invoke("Arduino", info);
                    
                    _portConnections.Remove("Arduino");
                }
            }
        }
        #endregion

        #region Query Methods
        /// <summary>
        /// Port durumunu sorgula
        /// </summary>
        public static PortConnectionInfo? GetPortStatus(string portKey)
        {
            lock (_lock)
            {
                return _portConnections.TryGetValue(portKey, out var info) ? info : null;
            }
        }

        /// <summary>
        /// Input port durumunu sorgula
        /// </summary>
        public static PortConnectionInfo? GetInputPortStatus()
        {
            return GetPortStatus("Input");
        }

        /// <summary>
        /// Output port durumunu sorgula
        /// </summary>
        public static PortConnectionInfo? GetOutputPortStatus()
        {
            return GetPortStatus("Output");
        }

        /// <summary>
        /// Arduino port durumunu sorgula
        /// </summary>
        public static PortConnectionInfo? GetArduinoPortStatus()
        {
            return GetPortStatus("Arduino");
        }

        /// <summary>
        /// Tüm port durumlarýný al
        /// </summary>
        public static Dictionary<string, PortConnectionInfo> GetAllPortStatuses()
        {
            lock (_lock)
            {
                return new Dictionary<string, PortConnectionInfo>(_portConnections);
            }
        }

        /// <summary>
        /// Herhangi bir port açýk mý kontrol et
        /// </summary>
        public static bool HasAnyConnectedPorts()
        {
            lock (_lock)
            {
                foreach (var connection in _portConnections.Values)
                {
                    if (connection.IsConnected) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Belirtilen port adý kullanýmda mý kontrol et
        /// </summary>
        public static bool IsPortInUse(string portName)
        {
            lock (_lock)
            {
                foreach (var connection in _portConnections.Values)
                {
                    if (connection.IsConnected && 
                        string.Equals(connection.PortName, portName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Port kullaným bilgisini al
        /// </summary>
        public static string GetPortUsageInfo(string portName)
        {
            lock (_lock)
            {
                foreach (var kvp in _portConnections)
                {
                    var connection = kvp.Value;
                    if (connection.IsConnected && 
                        string.Equals(connection.PortName, portName, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{kvp.Key} tarafýndan kullanýlýyor ({connection.Type})";
                    }
                }
                return "Kullanýmda deðil";
            }
        }
        #endregion

        #region Utility Methods
        /// <summary>
        /// Tüm port kayýtlarýný temizle (uygulama kapanýrken)
        /// </summary>
        public static void ClearAllPortRegistrations()
        {
            lock (_lock)
            {
                System.Diagnostics.Debug.WriteLine("?? GlobalPortManager: Tüm port kayýtlarý temizleniyor");
                _portConnections.Clear();
            }
        }

        /// <summary>
        /// Port durumu raporu oluþtur
        /// </summary>
        public static string GenerateStatusReport()
        {
            lock (_lock)
            {
                var report = new System.Text.StringBuilder();
                report.AppendLine("?? Port Durumu Raporu:");
                report.AppendLine("================================");

                if (_portConnections.Count == 0)
                {
                    report.AppendLine("? Hiç port baðlý deðil");
                }
                else
                {
                    foreach (var kvp in _portConnections)
                    {
                        var connection = kvp.Value;
                        var statusIcon = connection.IsConnected ? "?" : "?";
                        report.AppendLine($"{statusIcon} {kvp.Key}: {connection.PortName} @ {connection.BaudRate} ({connection.Type})");
                        report.AppendLine($"   ?? Baðlantý: {connection.ConnectedAt:HH:mm:ss}");
                        report.AppendLine($"   ?? Durum: {connection.StatusText}");
                        report.AppendLine();
                    }
                }

                return report.ToString();
            }
        }
        #endregion
    }
}