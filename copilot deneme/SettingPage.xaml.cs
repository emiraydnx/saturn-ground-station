using copilot_deneme.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using Windows.UI;

namespace copilot_deneme
{
    public sealed partial class SettingPage : Page
    {
        private readonly ChartViewModel _viewModel;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly SerialPortService _serialPortService;
        private readonly SerialPortService _outputPortService;
        private SerialPort? _arduinoSerialPort; // Arduino için ayrı SerialPort
        private bool _isArduinoConnected = false;

        public SettingPage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            
            // ViewModel oluştur ve ayarla
            _viewModel = new ChartViewModel();
            this.DataContext = _viewModel;
            
            // SerialPortService instance'larını oluştur
            _serialPortService = new SerialPortService();
            _outputPortService = new SerialPortService();
            
            // ViewModel ve Dispatcher'ı ayarla
            _serialPortService.ViewModel = _viewModel;
            _serialPortService.Dispatcher = _dispatcherQueue;
            _outputPortService.ViewModel = _viewModel;
            _outputPortService.Dispatcher = _dispatcherQueue;

            // Event handler'ları kaydet - TÜM EVENT'LERİ EKLE
            try
            {
                _serialPortService.OnHYIPacketReceived += OnHYIPacketReceived;
                _serialPortService.OnRocketDataUpdated += OnRocketDataUpdated;
                _serialPortService.OnPayloadDataUpdated += OnPayloadDataUpdated;
                _serialPortService.OnTelemetryDataUpdated += OnTelemetryDataUpdated;
                _serialPortService.OnError += OnSerialPortError;
                _outputPortService.OnError += OnOutputPortError;
                
                System.Diagnostics.Debug.WriteLine("SettingPage: Tüm event handler'lar başarıyla kaydedildi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage: Event handler kaydetme hatası: {ex.Message}");
            }
            
            // İlk yüklemede portları doldur
            RefreshAvailablePorts();
            RefreshArduinoPorts(); // Arduino portlarını da doldur
            
            // ComboBox'ları güvenli bir şekilde ayarla
            try
            {
                if (BaudRateComboBox_Input?.Items?.Count > 4)
                    BaudRateComboBox_Input.SelectedIndex = 4; // 115200
                else if (BaudRateComboBox_Input?.Items?.Count > 0)
                    BaudRateComboBox_Input.SelectedIndex = 0;
                    
                if (BaudRateComboBox_Output?.Items?.Count > 4)
                    BaudRateComboBox_Output.SelectedIndex = 4; // 115200
                else if (BaudRateComboBox_Output?.Items?.Count > 0)
                    BaudRateComboBox_Output.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BaudRate ComboBox ayarlama hatası: {ex.Message}");
            }
            
            System.Diagnostics.Debug.WriteLine("SettingPage başlatıldı - Instance-based SerialPortService kullanılıyor");
        }

        private void OnSerialPortError(string errorMessage)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Input.Text = $"Giriş Hatası: {errorMessage}";
                System.Diagnostics.Debug.WriteLine($"SettingPage Serial Port Hatası: {errorMessage}");
            });
        }

        private void OnOutputPortError(string errorMessage)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Output.Text = $"Çıkış Hatası: {errorMessage}";
                System.Diagnostics.Debug.WriteLine($"SettingPage Output Port Hatası: {errorMessage}");
            });
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            RefreshAvailablePorts();
            System.Diagnostics.Debug.WriteLine("Ports refreshed");
        }
        
        private void OnHYIPacketReceived(SerialPortService.HYITelemetryData data)
        {
            // HYI verisi alındığında log
            _dispatcherQueue.TryEnqueue(() =>
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage: HYI Data - Team: {data.TeamId}, Counter: {data.PacketCounter}");
            });
        }

        private void OnRocketDataUpdated(SerialPortService.RocketTelemetryData data)
        {
            // Roket verisi alındığında log
            _dispatcherQueue.TryEnqueue(() =>
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage: ROKET VERİSİ ALINDI! Paket #{data.PacketCounter}, İrtifa: {data.RocketAltitude:F1}m");
                
                // Status text'i güncelle
                StatusText_Input.Text = $"ROKET #{data.PacketCounter} - İrtifa: {data.RocketAltitude:F1}m";
            });
        }

        private void OnPayloadDataUpdated(SerialPortService.PayloadTelemetryData data)
        {
            // Payload verisi alındığında log
            _dispatcherQueue.TryEnqueue(() =>
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage: Payload Data - Counter: {data.PacketCounter}, İrtifa: {data.PayloadAltitude:F1}m");
            });
        }

        private void OnTelemetryDataUpdated(SerialPortService.RocketTelemetryData rocketData, SerialPortService.PayloadTelemetryData payloadData)
        {
            // Telemetri verisi kombinasyonu alındığında log
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    string rocketInfo = rocketData != null ? $"Roket: {rocketData.RocketAltitude:F1}m" : "Roket: null";
                    string payloadInfo = payloadData != null ? $"Payload: {payloadData.PayloadAltitude:F1}m" : "Payload: null";
                    
                    System.Diagnostics.Debug.WriteLine($"SettingPage: TELEMETRİ GÜNCELLEME - {rocketInfo}, {payloadInfo}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SettingPage: Telemetri güncelleme hatası: {ex.Message}");
                }
            });
        }

        private void RefreshAvailablePorts()
        {
            try
            {
                string[] availablePorts = SerialPort.GetPortNames();
                var inputSelection = PortComboBox_Input.SelectedItem as string;
                var outputSelection = PortComboBox_Output.SelectedItem as string;

                PortComboBox_Input.ItemsSource = availablePorts;
                PortComboBox_Output.ItemsSource = availablePorts;

                if (availablePorts.Contains(inputSelection)) PortComboBox_Input.SelectedItem = inputSelection;
                else if (availablePorts.Length > 0) PortComboBox_Input.SelectedIndex = 0;

                if (availablePorts.Contains(outputSelection)) PortComboBox_Output.SelectedItem = outputSelection;
                else if (availablePorts.Length > 1) PortComboBox_Output.SelectedIndex = 1;
                else if (availablePorts.Length > 0) PortComboBox_Output.SelectedIndex = 0;

                StatusText_Input.Text = $"{availablePorts.Length} port bulundu.";
                StatusText_Output.Text = $"{availablePorts.Length} port bulundu.";
            }
            catch (Exception ex)
            {
                StatusText_Input.Text = $"Port listesi alınamadı: {ex.Message}";
                StatusText_Output.Text = $"Port listesi alınamadı: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Port listesi alma hatası: {ex.Message}");
            }
        }

        private async void ConnectInputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var portName = PortComboBox_Input.SelectedItem as string;
                if (string.IsNullOrEmpty(portName)) 
                    throw new InvalidOperationException("Giriş için bir port seçin.");
                
                // BaudRate ComboBox'tan güvenli değer alma
                string baudRateStr = "115200"; // Default
                try
                {
                    var selectedBaudItem = BaudRateComboBox_Input.SelectedItem as ComboBoxItem;
                    if (selectedBaudItem?.Content != null)
                    {
                        baudRateStr = selectedBaudItem.Content.ToString();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"BaudRate alma hatası: {ex.Message}");
                }
                
                var baudRate = int.Parse(baudRateStr);

                // Async olarak bağlan
                await _serialPortService.InitializeAsync(portName, baudRate);
                await _serialPortService.StartReadingAsync();

                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.LightGreen);
                StatusText_Input.Text = $"Bağlandı: {portName} ({baudRate})";
                
                System.Diagnostics.Debug.WriteLine($"Input port bağlandı: {portName} @ {baudRate}");
            }
            catch (Exception ex)
            {
                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Input.Text = $"Bağlantı Hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Input port bağlantı hatası: {ex.Message}");
            }
        }

        private async void DisconnectInputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _serialPortService.StopReadingAsync();
                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Input.Text = "Giriş Portu Kapalı";
                System.Diagnostics.Debug.WriteLine("Input port kapatıldı");
            }
            catch (Exception ex)
            {
                StatusText_Input.Text = $"Kapatma Hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Input port kapatma hatası: {ex.Message}");
            }
        }

        private async void ConnectOutputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var portName = PortComboBox_Output.SelectedItem as string;
                if (string.IsNullOrEmpty(portName))
                    throw new InvalidOperationException("Çıkış için bir port seçin.");

                // BaudRate ComboBox'tan güvenli değer alma
                string baudRateStr = "115200"; // Default
                try
                {
                    var selectedBaudItem = BaudRateComboBox_Output.SelectedItem as ComboBoxItem;
                    if (selectedBaudItem?.Content != null)
                    {
                        baudRateStr = selectedBaudItem.Content.ToString();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Output BaudRate alma hatası: {ex.Message}");
                }
                
                var baudRate = int.Parse(baudRateStr);

                // Output port için ayrı SerialPortService instance kullan
                await _outputPortService.InitializeAsync(portName, baudRate);
                await _outputPortService.StartReadingAsync();

                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.LightGreen);
                StatusText_Output.Text = $"HYI Output Aktif: {portName} ({baudRate})";

                System.Diagnostics.Debug.WriteLine($"Output port bağlandı: {portName} @ {baudRate}");
            }
            catch (Exception ex)
            {
                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Output.Text = $"Output Hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Output port bağlantı hatası: {ex.Message}");
            }
        }

        private async void DisconnectOutputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _outputPortService.StopReadingAsync();
                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Output.Text = "Çıkış Portu Kapalı";
                System.Diagnostics.Debug.WriteLine("Output port kapatıldı");
            }
            catch (Exception ex)
            {
                StatusText_Output.Text = $"Kapatma Hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Output port kapatma hatası: {ex.Message}");
            }
        }

        private void StartHyiTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Seçilen interval değerini al
                var selectedItem = HyiIntervalComboBox.SelectedItem as ComboBoxItem;
                int interval = selectedItem?.Tag != null ? int.Parse(selectedItem.Tag.ToString()) : 2000;
                
                // HYI test modunu başlat
                _serialPortService.StartHyiTestMode(interval);
                
                // UI güncellemeleri
                HyiTestStatusIndicator.Fill = new SolidColorBrush(Colors.Green);
                HyiTestStatusText.Text = "Çalışıyor";
                StartHyiTestButton.IsEnabled = false;
                StopHyiTestButton.IsEnabled = true;
                
                System.Diagnostics.Debug.WriteLine($"HYI Test modu başlatıldı - Interval: {interval}ms");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HYI Test başlatma hatası: {ex.Message}");
                HyiTestStatusText.Text = $"Hata: {ex.Message}";
            }
        }

        private void StopHyiTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // HYI test modunu durdur
                _serialPortService.StopHyiTestMode();
                
                // UI güncellemeleri
                HyiTestStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                HyiTestStatusText.Text = "Durduruldu";
                StartHyiTestButton.IsEnabled = true;
                StopHyiTestButton.IsEnabled = false;
                
                System.Diagnostics.Debug.WriteLine("HYI Test modu durduruldu");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HYI Test durdurma hatası: {ex.Message}");
                HyiTestStatusText.Text = $"Hata: {ex.Message}";
            }
        }

        // Static accessor methods for backward compatibility with other pages
        public static SerialPortService? GetInputSerialPortService()
        {
            // Bu method diğer sayfalardan erişim için kullanılabilir
            // Instance'ı saklamak için static property ekleyebiliriz
            return _currentInputInstance;
        }

        // Arduino SerialPort için static accessor
        public static SerialPort? GetArduinoSerialPortService()
        {
            return _currentArduinoInstance;
        }

        // Arduino verisi güncellemek için ArduinoPage'e erişim
        public static Action<float, float, float, float, float, float, float, float>? ArduinoDataUpdateCallback { get; set; }

        private static SerialPortService? _currentInputInstance;
        private static SerialPort? _currentArduinoInstance;

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // Global erişim için instance'ı kaydet
            _currentInputInstance = _serialPortService;
            _currentArduinoInstance = _arduinoSerialPort; // Arduino instance'ını da kaydet
            System.Diagnostics.Debug.WriteLine("SettingPage navigasyonu tamamlandı");
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // Event handler'ları kaldır - TÜM EVENT'LERİ KALDIR
            _serialPortService.OnHYIPacketReceived -= OnHYIPacketReceived;
            _serialPortService.OnRocketDataUpdated -= OnRocketDataUpdated;
            _serialPortService.OnPayloadDataUpdated -= OnPayloadDataUpdated;
            _serialPortService.OnTelemetryDataUpdated -= OnTelemetryDataUpdated;
            _serialPortService.OnError -= OnSerialPortError;
            _outputPortService.OnError -= OnOutputPortError;
            
            System.Diagnostics.Debug.WriteLine("SettingPage'den ayrıldı - Event handler'lar kaldırıldı");
        }

        public async void Dispose()
        {
            try
            {
                await _serialPortService.DisposeAsync();
                await _outputPortService.DisposeAsync();
                
                // Arduino bağlantısını da kapat
                if (_arduinoSerialPort?.IsOpen == true)
                {
                    _arduinoSerialPort.Close();
                }
                _arduinoSerialPort?.Dispose();
                
                System.Diagnostics.Debug.WriteLine("SettingPage SerialPortService instance'ları dispose edildi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage dispose hatası: {ex.Message}");
            }
        }

        // Arduino Port Management Methods
        private void RefreshArduinoPorts()
        {
            try
            {
                string[] availablePorts = SerialPort.GetPortNames();
                var currentSelection = ArduinoPortComboBox.SelectedItem as string;

                ArduinoPortComboBox.ItemsSource = availablePorts;

                if (availablePorts.Contains(currentSelection))
                    ArduinoPortComboBox.SelectedItem = currentSelection;
                else if (availablePorts.Length > 0)
                    ArduinoPortComboBox.SelectedIndex = 0;

                System.Diagnostics.Debug.WriteLine($"Arduino portları yenilendi: {availablePorts.Length} port bulundu");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino port listesi alma hatası: {ex.Message}");
            }
        }

        private void RefreshArduinoPorts_Click(object sender, RoutedEventArgs e)
        {
            RefreshArduinoPorts();
        }

        private async void ConnectArduino_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var portName = ArduinoPortComboBox.SelectedItem as string;
                if (string.IsNullOrEmpty(portName))
                    throw new InvalidOperationException("Arduino için bir port seçin.");

                // BaudRate değerini al
                string baudRateStr = "115200"; // Default
                try
                {
                    var selectedBaudItem = ArduinoBaudRateComboBox.SelectedItem as ComboBoxItem;
                    if (selectedBaudItem?.Content != null)
                    {
                        baudRateStr = selectedBaudItem.Content.ToString();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Arduino BaudRate alma hatası: {ex.Message}");
                }

                var baudRate = int.Parse(baudRateStr);

                // Mevcut bağlantıyı kapat
                if (_arduinoSerialPort?.IsOpen == true)
                {
                    _arduinoSerialPort.Close();
                }
                _arduinoSerialPort?.Dispose();

                // Yeni bağlantı oluştur
                _arduinoSerialPort = new SerialPort(portName, baudRate)
                {
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = 2000,
                    WriteTimeout = 2000
                };

                _arduinoSerialPort.DataReceived += ArduinoSerialPort_DataReceived;
                _arduinoSerialPort.Open();

                _isArduinoConnected = true;
                ArduinoStatusIndicator.Fill = new SolidColorBrush(Colors.LightGreen);
                ArduinoStatusText.Text = $"Bağlı: {portName}";
                ConnectArduinoButton.IsEnabled = false;
                DisconnectArduinoButton.IsEnabled = true;

                // Global instance'ı güncelle
                _currentArduinoInstance = _arduinoSerialPort;

                System.Diagnostics.Debug.WriteLine($"Arduino manuel olarak bağlandı: {portName} @ {baudRate}");
            }
            catch (Exception ex)
            {
                ArduinoStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                ArduinoStatusText.Text = $"Hata: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Arduino bağlantı hatası: {ex.Message}");
            }
        }

        private void DisconnectArduino_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_arduinoSerialPort?.IsOpen == true)
                {
                    _arduinoSerialPort.Close();
                }
                _arduinoSerialPort?.Dispose();
                _arduinoSerialPort = null;

                _isArduinoConnected = false;
                ArduinoStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                ArduinoStatusText.Text = "Bağlı Değil";
                ConnectArduinoButton.IsEnabled = true;
                DisconnectArduinoButton.IsEnabled = false;

                // Global instance'ı temizle
                _currentArduinoInstance = null;

                // Test verilerini sıfırla
                ArduinoYawText.Text = "Yaw: --°";
                ArduinoPitchText.Text = "Pitch: --°";
                ArduinoRollText.Text = "Roll: --°";
                ArduinoAccelText.Text = "İvme: -- m/s²";
                ArduinoPressureText.Text = "Basınç: -- hPa";
                ArduinoAltitudeText.Text = "İrtifa: -- m";
                ArduinoTemperatureText.Text = "Sıcaklık: --°C";
                ArduinoPressureRateText.Text = "dP/dt: -- hPa/s";

                System.Diagnostics.Debug.WriteLine("Arduino bağlantısı kapatıldı");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino kapatma hatası: {ex.Message}");
            }
        }

        private void ArduinoSerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_arduinoSerialPort?.IsOpen != true) return;

                string data = _arduinoSerialPort.ReadLine().Trim();

                if (!string.IsNullOrEmpty(data))
                {
                    ParseAndDisplayArduinoData(data);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino veri okuma hatası: {ex.Message}");
            }
        }

        private void ParseAndDisplayArduinoData(string data)
        {
            try
            {
                // Debug için ham veriyi logla
                System.Diagnostics.Debug.WriteLine($"SettingPage Arduino HAM VERİ: {data}");
                
                // Arduino çıktı formatını parse et
                var yawMatch = System.Text.RegularExpressions.Regex.Match(data, @"Yaw:\s*([\d\.-]+)");
                var pitchMatch = System.Text.RegularExpressions.Regex.Match(data, @"Pitch:\s*([\d\.-]+)");
                var rollMatch = System.Text.RegularExpressions.Regex.Match(data, @"Roll:\s*([\d\.-]+)");
                var accelMatch = System.Text.RegularExpressions.Regex.Match(data, @"Accel:\s*([\d\.-]+)");
                var pressureMatch = System.Text.RegularExpressions.Regex.Match(data, @"Pressure:\s*([\d\.-]+)");
                var altitudeMatch = System.Text.RegularExpressions.Regex.Match(data, @"Altitude:\s*([\d\.-]+)");
                var temperatureMatch = System.Text.RegularExpressions.Regex.Match(data, @"Temperature:\s*([\d\.-]+)");
                var pressureRateMatch = System.Text.RegularExpressions.Regex.Match(data, @"PressureRate:\s*([\d\.-]+)");

                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (yawMatch.Success && float.TryParse(yawMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw))
                        ArduinoYawText.Text = $"Yaw: {yaw:F1}°";

                    if (pitchMatch.Success && float.TryParse(pitchMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pitch))
                        ArduinoPitchText.Text = $"Pitch: {pitch:F1}°";

                    if (rollMatch.Success && float.TryParse(rollMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float roll))
                        ArduinoRollText.Text = $"Roll: {roll:F1}°";

                    if (accelMatch.Success && float.TryParse(accelMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float accel))
                        ArduinoAccelText.Text = $"İvme: {accel:F2} m/s²";

                    if (pressureMatch.Success && float.TryParse(pressureMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pressure))
                        ArduinoPressureText.Text = $"Basınç: {pressure:F2} hPa";

                    if (altitudeMatch.Success && float.TryParse(altitudeMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float altitude))
                    {
                        // İrtifa değerini makul sınırlar içinde kontrol et
                        if (altitude >= -1000 && altitude <= 50000)
                        {
                            ArduinoAltitudeText.Text = $"İrtifa: {altitude:F1} m";
                        }
                        else
                        {
                            ArduinoAltitudeText.Text = $"İrtifa: HATA ({altitude:F1})";
                            System.Diagnostics.Debug.WriteLine($"SettingPage İrtifa DİKKAT: Aşırı değer: {altitude}m");
                        }
                    }

                    if (temperatureMatch.Success && float.TryParse(temperatureMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float temperature))
                    {
                        // Sıcaklık değerini makul sınırlar içinde kontrol et
                        if (temperature >= -50 && temperature <= 85)
                        {
                            ArduinoTemperatureText.Text = $"Sıcaklık: {temperature:F1}°C";
                        }
                        else
                        {
                            ArduinoTemperatureText.Text = $"Sıcaklık: HATA ({temperature:F1})";
                            System.Diagnostics.Debug.WriteLine($"SettingPage Sıcaklık DİKKAT: Aşırı değer: {temperature}°C");
                        }
                    }

                    if (pressureRateMatch.Success && float.TryParse(pressureRateMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pressureRate))
                        ArduinoPressureRateText.Text = $"dP/dt: {pressureRate:F3} hPa/s";

                    // ArduinoPage callback'ini çağır (eğer ArduinoPage açıksa chart'larını güncelle)
                    if (ArduinoDataUpdateCallback != null)
                    {
                        float yawVal = 0, pitchVal = 0, rollVal = 0, accelVal = 0, pressureVal = 0, altitudeVal = 0, temperatureVal = 0, pressureRateVal = 0;
                        
                        if (yawMatch.Success && float.TryParse(yawMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) 
                            yawVal = y;
                        if (pitchMatch.Success && float.TryParse(pitchMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float p)) 
                            pitchVal = p;
                        if (rollMatch.Success && float.TryParse(rollMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float r)) 
                            rollVal = r;
                        if (accelMatch.Success && float.TryParse(accelMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float a)) 
                            accelVal = a;
                        if (pressureMatch.Success && float.TryParse(pressureMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pr)) 
                            pressureVal = pr;
                        if (altitudeMatch.Success && float.TryParse(altitudeMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float alt) && alt >= -1000 && alt <= 50000) 
                            altitudeVal = alt;
                        if (temperatureMatch.Success && float.TryParse(temperatureMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float temp) && temp >= -50 && temp <= 85) 
                            temperatureVal = temp;
                        if (pressureRateMatch.Success && float.TryParse(pressureRateMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pRate)) 
                            pressureRateVal = pRate;

                        try
                        {
                            ArduinoDataUpdateCallback(yawVal, pitchVal, rollVal, accelVal, pressureVal, altitudeVal, temperatureVal, pressureRateVal);
                            System.Diagnostics.Debug.WriteLine($"SettingPage: ArduinoPage callback çağrıldı - İrtifa: {altitudeVal:F1}m, Sıcaklık: {temperatureVal:F1}°C");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"SettingPage: ArduinoPage callback hatası: {ex.Message}");
                        }
                    }
                });

                System.Diagnostics.Debug.WriteLine($"SettingPage Arduino verisi parse edildi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage Arduino veri parse hatası: {ex.Message}");
            }
        }
    }
}