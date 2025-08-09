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

// ✨ YENİ: Ayrıştırılan data türlerini ekle
using copilot_deneme.TelemetryData;
using copilot_deneme.Services;

namespace copilot_deneme
{
    public sealed partial class SettingPage : Page
    {
        private readonly ChartViewModel _viewModel;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly SerialPortService _serialPortService;
        // 🗑️ Arduino kısmı kaldırıldı - private SerialPort? _arduinoSerialPort;
        // 🗑️ Arduino kısmı kaldırıldı - private bool _isArduinoConnected = false;

        public SettingPage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            
            // ViewModel oluştur ve ayarla
            _viewModel = new ChartViewModel();
            this.DataContext = _viewModel;
            
            // SerialPortService instance'ını oluştur
            _serialPortService = new SerialPortService();
            
            // ViewModel ve Dispatcher'ı ayarla
            _serialPortService.ViewModel = _viewModel;
            _serialPortService.Dispatcher = _dispatcherQueue;

            // ✨ YENİ: GlobalPortManager event'lerini dinle
            GlobalPortManager.OnPortStatusChanged += OnGlobalPortStatusChanged;

            // Event handler'ları kaydet - TÜM EVENT'LERİ EKLE
            try
            {
                _serialPortService.OnHYIPacketReceived += OnHYIPacketReceived;
                _serialPortService.OnRocketDataUpdated += OnRocketDataUpdated;
                _serialPortService.OnPayloadDataUpdated += OnPayloadDataUpdated;
                _serialPortService.OnTelemetryDataUpdated += OnTelemetryDataUpdated;
                _serialPortService.OnError += OnSerialPortError;
                
                System.Diagnostics.Debug.WriteLine("SettingPage: Tüm event handler'lar başarıyla kaydedildi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage: Event handler kaydetme hatası: {ex.Message}");
            }
            
            // İlk yüklemede portları doldur
            RefreshAvailablePorts();
            
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

            // ✨ YENİ: Sayfa yüklendiğinde mevcut port durumlarını kontrol et
            CheckExistingPortConnections();
            
            System.Diagnostics.Debug.WriteLine("SettingPage başlatıldı - Instance-based SerialPortService kullanılıyor + GlobalPortManager");
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
        
        private void OnHYIPacketReceived(HYITelemetryData data)
        {
            // HYI verisi alındığında log
            _dispatcherQueue.TryEnqueue(() =>
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage: HYI Data - Team: {data.TeamId}, Counter: {data.PacketCounter}");
            });
        }

        private void OnRocketDataUpdated(RocketTelemetryData data)
        {
            // Roket verisi alındığında log
            _dispatcherQueue.TryEnqueue(() =>
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage: ROKET VERİSİ ALINDI! Paket #{data.PacketCounter}, İrtifa: {data.RocketAltitude:F1}m");
                
                // Status text'i güncelle
                StatusText_Input.Text = $"ROKET #{data.PacketCounter} - İrtifa: {data.RocketAltitude:F1}m";
            });
        }

        private void OnPayloadDataUpdated(PayloadTelemetryData data)
        {
            // Payload verisi alındığında log
            _dispatcherQueue.TryEnqueue(() =>
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage: Payload Data - Counter: {data.PacketCounter}, İrtifa: {data.PayloadAltitude:F1}m");
            });
        }

        private void OnTelemetryDataUpdated(RocketTelemetryData rocketData, PayloadTelemetryData payloadData)
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

                // ✨ YENİ: Port kullanımda mı kontrol et
                if (GlobalPortManager.IsPortInUse(portName))
                {
                    var usageInfo = GlobalPortManager.GetPortUsageInfo(portName);
                    StatusText_Input.Text = $"❌ Port zaten {usageInfo}";
                    System.Diagnostics.Debug.WriteLine($"Port kullanım engeli: {portName} - {usageInfo}");
                    return;
                }
                
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

                // ✨ YENİ: GlobalPortManager'a kaydet
                GlobalPortManager.RegisterInputPort(portName, baudRate, _serialPortService);
                
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
                
                // ✨ YENİ: GlobalPortManager'dan kayıt kaldır
                GlobalPortManager.UnregisterInputPort();
                
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

                // ✨ YENİ: Port kullanımda mı kontrol et
                if (GlobalPortManager.IsPortInUse(portName))
                {
                    var usageInfo = GlobalPortManager.GetPortUsageInfo(portName);
                    StatusText_Output.Text = $"❌ Port zaten {usageInfo}";
                    System.Diagnostics.Debug.WriteLine($"Port kullanım engeli: {portName} - {usageInfo}");
                    return;
                }

                // BaudRate ComboBox'tan güvenli değer alma
                string baudRateStr = "19200"; // Default
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

                // Input SerialPortService'in output port'unu başlat
                await _serialPortService.InitializeOutputPortAsync(portName, baudRate);

                // ✨ YENİ: GlobalPortManager'a kaydet
                GlobalPortManager.RegisterOutputPort(portName, baudRate, _serialPortService);

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
                await _serialPortService.CloseOutputPortAsync();
                
                // ✨ YENİ: GlobalPortManager'dan kayıt kaldır
                GlobalPortManager.UnregisterOutputPort();
                
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
                
                // HYI test modunu başlat (bu otomatik olarak IsAutoHyiGenerationEnabled'ı da true yapar)
                _serialPortService.StartHyiTestMode(interval);
                
                // UI güncellemeleri
                HyiTestStatusIndicator.Fill = new SolidColorBrush(Colors.Green);
                HyiTestStatusText.Text = "Çalışıyor";
                StartHyiTestButton.IsEnabled = false;
                StopHyiTestButton.IsEnabled = true;
                
                // Toggle'ı otomatik olarak aktif yap
                AutoHyiGenerationToggle.IsOn = true;
                
                System.Diagnostics.Debug.WriteLine($"HYI Test modu başlatıldı - Interval: {interval}ms, Arduino->HYI üretimi aktif");
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
                
                // Otomatik HYI üretimi de kapanacak, toggle'ı güncelle
                AutoHyiGenerationToggle.IsOn = false;
                
                System.Diagnostics.Debug.WriteLine("HYI Test modu durduruldu");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HYI Test durdurma hatası: {ex.Message}");
                HyiTestStatusText.Text = $"Hata: {ex.Message}";
            }
        }

        /// <summary>
        /// ✨ YENİ: Arduino verilerinden otomatik HYI üretimi toggle kontrolü
        /// </summary>
        private void AutoHyiGeneration_Toggled(object sender, RoutedEventArgs e)
        {
            try
            {
                var toggle = sender as ToggleSwitch;
                if (toggle != null && _serialPortService != null)
                {
                    _serialPortService.IsAutoHyiGenerationEnabled = toggle.IsOn;
                    
                    string statusMessage = toggle.IsOn 
                        ? "✅ Arduino verilerinden otomatik HYI üretimi AKTİF!" 
                        : "❌ Arduino verilerinden otomatik HYI üretimi KAPALI!";
                        
                    System.Diagnostics.Debug.WriteLine($"Auto HYI Generation: {toggle.IsOn}");
                    System.Diagnostics.Debug.WriteLine(statusMessage);
                    
                    // Bilgilendirme mesajı göster
                    if (toggle.IsOn)
                    {
                        // Output port kontrol et
                        if (!_serialPortService.IsOutputPortOpen())
                        {
                            System.Diagnostics.Debug.WriteLine("⚠️ UYARI: Output port açık değil! HYI paketleri gönderilemez.");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("✅ Output port açık, Arduino verilerinden HYI paketleri gönderilecek.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto HYI Generation toggle hatası: {ex.Message}");
            }
        }

        // Manuel test paketi gönderme butonları için event handler'lar
        private async void SendManualHyiTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success = await _serialPortService.SendManualHyiTestPacket();
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("Manuel HYI test paketi başarıyla gönderildi");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Manuel HYI test paketi gönderme hatası: {ex.Message}");
            }
        }

        private async void SendTestRocket_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success = await _serialPortService.SendTestRocketPacket();
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("Test roket paketi başarıyla gönderildi");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Test roket paketi gönderme hatası: {ex.Message}");
            }
        }

        private async void SendTestPayload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success = await _serialPortService.SendTestPayloadPacket();
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("Test payload paketi başarıyla gönderildi");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Test payload paketi gönderme hatası: {ex.Message}");
            }
        }

        // Özel HYI paket gönderme metodları
        private async void SendCustomHyiPacket_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Örnek verilerle özel HYI paketi gönder (verdiğiniz sıraya göre)
                bool success = await _serialPortService.SendCustomHyiPacket(
                    teamId: 123,           // Takım ID
                    packetCounter: 1,      // Sayaç
                    altitude: 150.5f,      // İrtifa
                    rocketGpsAltitude: 152.3f,  // Roket GPS İrtifa
                    rocketLatitude: 39.925533f, // Roket Enlem
                    rocketLongitude: 32.866287f, // Roket Boylam
                    payloadGpsAltitude: 148.7f,  // Görev Yükü GPS İrtifa
                    payloadLatitude: 39.925000f, // Görev Yükü Enlem
                    payloadLongitude: 32.866000f, // Görev Yükü Boylam
                    stageGpsAltitude: 145.2f,    // Kademe GPS İrtifa
                    stageLatitude: 39.924500f,   // Kademe Enlem
                    stageLongitude: 32.865500f,  // Kademe Boylam
                    gyroscopeX: 15.3f,      // Jiroskop X
                    gyroscopeY: -8.7f,      // Jiroskop Y
                    gyroscopeZ: 22.1f,      // Jiroskop Z
                    accelerationX: 2.1f,    // İvme X
                    accelerationY: -1.5f,   // İvme Y
                    accelerationZ: 9.81f,   // İvme Z
                    angle: 87.5f,          // Açı
                    status: 3              // Durum
                );
                
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("Özel HYI paketi başarıyla gönderildi");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Özel HYI paketi gönderme hatası: {ex.Message}");
            }
        }

        private async void SendZeroHyiPacket_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Tüm değerleri sıfır olan test paketi gönder
                bool success = await _serialPortService.SendZeroValueHyiPacket();
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("Sıfır değerli HYI test paketi başarıyla gönderildi");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sıfır değerli HYI test paketi gönderme hatası: {ex.Message}");
            }
        }

        // Debug test paketleri için yeni event handler'lar
        private async void SendDebugHyiPacket_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Debug amaçlı özel değerlerle test paketi
                bool success = await _serialPortService.SendCustomHyiPacket(
                    teamId: 99,            // Debug takım ID
                    packetCounter: 255,    // Max sayaç değeri
                    altitude: 1234.56f,    // Test irtifa
                    rocketGpsAltitude: 1234.78f,
                    rocketLatitude: 40.123456f,
                    rocketLongitude: 29.123456f,
                    payloadGpsAltitude: 1200.12f,
                    payloadLatitude: 40.120000f,
                    payloadLongitude: 29.120000f,
                    stageGpsAltitude: 1100.99f,
                    stageLatitude: 40.110000f,
                    stageLongitude: 29.110000f,
                    gyroscopeX: 123.45f,
                    gyroscopeY: -67.89f,
                    gyroscopeZ: 180.0f,
                    accelerationX: 9.81f,
                    accelerationY: -4.52f,
                    accelerationZ: 15.67f,
                    angle: 359.99f,
                    status: 255           // Max durum değeri
                );
                
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("Debug HYI paketi başarıyla gönderildi");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debug HYI paketi gönderme hatası: {ex.Message}");
            }
        }

        // Static accessor methods for backward compatibility with other pages
        public static SerialPortService? GetInputSerialPortService()
        {
            // Bu method diğer sayfalardan erişim için kullanılabilir
            // Instance'ı saklamak için static property ekleyebiliriz
            return _currentInputInstance;
        }

        private static SerialPortService? _currentInputInstance;

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // Global erişim için instance'ı kaydet
            _currentInputInstance = _serialPortService;
            // 🗑️ Arduino kısmı kaldırıldı - _currentArduinoInstance = _arduinoSerialPort;
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

            // ✨ YENİ: GlobalPortManager event handler'ını kaldır
            GlobalPortManager.OnPortStatusChanged -= OnGlobalPortStatusChanged;
            
            System.Diagnostics.Debug.WriteLine("SettingPage'den ayrıldı - Event handler'lar kaldırıldı");
        }

        public async void Dispose()
        {
            try
            {
                // ✨ YENİ: GlobalPortManager event handler'ını kaldır
                GlobalPortManager.OnPortStatusChanged -= OnGlobalPortStatusChanged;

                await _serialPortService.DisposeAsync();
                
                // 🗑️ Arduino bağlantısını da kapat - kaldırıldı
                // if (_arduinoSerialPort?.IsOpen == true)
                // {
                //     _arduinoSerialPort.Close();
                // }
                // _arduinoSerialPort?.Dispose();

                // ✨ YENİ: GlobalPortManager kayıtlarını temizle
                GlobalPortManager.UnregisterInputPort();
                GlobalPortManager.UnregisterOutputPort();
                // 🗑️ Arduino kısmı kaldırıldı - GlobalPortManager.UnregisterArduinoPort();
                
                System.Diagnostics.Debug.WriteLine("SettingPage SerialPortService instance'ları dispose edildi + GlobalPortManager temizlendi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage dispose hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// ✨ YENİ: GlobalPortManager'dan gelen port durumu değişikliklerini işle
        /// </summary>
        private void OnGlobalPortStatusChanged(string portKey, GlobalPortManager.PortConnectionInfo connectionInfo)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔌 SettingPage: Port durumu değişti - {portKey}: {connectionInfo.StatusText}");

                    switch (portKey)
                    {
                        case "Input":
                            UpdateInputPortUI(connectionInfo);
                            break;
                        case "Output":
                            UpdateOutputPortUI(connectionInfo);
                            break;
            
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Port durumu güncelleme hatası: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// ✨ YENİ: Input port UI'sını güncelle
        /// </summary>
        private void UpdateInputPortUI(GlobalPortManager.PortConnectionInfo connectionInfo)
        {
            if (connectionInfo.IsConnected)
            {
                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.LightGreen);
                StatusText_Input.Text = connectionInfo.StatusText;
                // XAML'deki butonların x:Name'leri yoktu, doğrudan içerik değiştirebiliriz
                // ConnectInputPortButton.IsEnabled = false;
                // DisconnectInputPortButton.IsEnabled = true;
                
                // ComboBox'ları disable et
                PortComboBox_Input.IsEnabled = false;
                BaudRateComboBox_Input.IsEnabled = false;
            }
            else
            {
                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Input.Text = connectionInfo.StatusText;
                // ConnectInputPortButton.IsEnabled = true;
                // DisconnectInputPortButton.IsEnabled = false;
                
                // ComboBox'ları enable et
                PortComboBox_Input.IsEnabled = true;
                BaudRateComboBox_Input.IsEnabled = true;
            }
        }

        /// <summary>
        /// ✨ YENİ: Output port UI'sını güncelle
        /// </summary>
        private void UpdateOutputPortUI(GlobalPortManager.PortConnectionInfo connectionInfo)
        {
            if (connectionInfo.IsConnected)
            {
                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.LightGreen);
                StatusText_Output.Text = connectionInfo.StatusText;
                // ConnectOutputPortButton.IsEnabled = false;
                // DisconnectOutputPortButton.IsEnabled = true;
                
                // ComboBox'ları disable et
                PortComboBox_Output.IsEnabled = false;
                BaudRateComboBox_Output.IsEnabled = false;
            }
            else
            {
                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Output.Text = connectionInfo.StatusText;
                // ConnectOutputPortButton.IsEnabled = true;
                // DisconnectOutputPortButton.IsEnabled = false;
                
                // ComboBox'ları enable et
                PortComboBox_Output.IsEnabled = true;
                BaudRateComboBox_Output.IsEnabled = true;
            }
        }

        /// <summary>
        /// ✨ YENİ: Sayfa yüklendiğinde mevcut port bağlantılarını kontrol et
        /// </summary>
        private void CheckExistingPortConnections()
        {
            try
            {
                // Input port durumunu kontrol et
                var inputPortStatus = GlobalPortManager.GetInputPortStatus();
                if (inputPortStatus != null)
                {
                    UpdateInputPortUI(inputPortStatus);
                    // Mevcut port seçimini güncelle
                    if (PortComboBox_Input.ItemsSource is string[] ports && ports.Contains(inputPortStatus.PortName))
                    {
                        PortComboBox_Input.SelectedItem = inputPortStatus.PortName;
                    }
                }

                // Output port durumunu kontrol et
                var outputPortStatus = GlobalPortManager.GetOutputPortStatus();
                if (outputPortStatus != null)
                {
                    UpdateOutputPortUI(outputPortStatus);
                    // Mevcut port seçimini güncelle
                    if (PortComboBox_Output.ItemsSource is string[] ports && ports.Contains(outputPortStatus.PortName))
                    {
                        PortComboBox_Output.SelectedItem = outputPortStatus.PortName;
                    }
                }

                // 🗑️ Arduino port durumunu kontrol et - kaldırıldı

                // ✅ Durum raporu
                System.Diagnostics.Debug.WriteLine(GlobalPortManager.GenerateStatusReport());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mevcut port bağlantıları kontrol hatası: {ex.Message}");
            }
        }

    }
}