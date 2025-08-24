using copilot_deneme.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;

namespace copilot_deneme
{
    public sealed partial class SettingPage : Page
    {
        private readonly ChartViewModel _viewModel;
        private readonly DispatcherQueue _dispatcherQueue;

        public SettingPage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            
            // ViewModel oluştur ve ayarla
            _viewModel = new ChartViewModel();
            this.DataContext = _viewModel;
            
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
                LogDebug($"BaudRate ComboBox ayarlama hatası: {ex.Message}");
            }
            
            LogDebug("SettingPage başlatıldı - SerialPortManager kullanılıyor");
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            RefreshAvailablePorts();
            LogDebug("Ports refreshed");
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
                LogDebug($"Port listesi alma hatası: {ex.Message}");
            }
        }

        private async void ConnectInputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var portName = PortComboBox_Input.SelectedItem as string;
                if (string.IsNullOrEmpty(portName)) 
                    throw new InvalidOperationException("Giriş için bir port seçin.");

                // Output port ile aynı port'u seçmeye çalışıyorsa uyar
                var outputPortName = PortComboBox_Output.SelectedItem as string;
                if (!string.IsNullOrEmpty(outputPortName) &&
                    portName == outputPortName &&
                    SerialPortManager.Instance.IsOutputConnected)
                {
                    StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Orange);
                    StatusText_Input.Text = "Input port, Output port ile aynı olamaz. Farklı bir port seçin.";
                    LogDebug($"Port çakışması: Input ve Output için aynı port seçildi: {portName}");
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
                    LogDebug($"BaudRate alma hatası: {ex.Message}");
                }
                
                var baudRate = int.Parse(baudRateStr);

                // SerialPortManager kullanarak bağlan
                await SerialPortManager.Instance.InitializeAsync(portName, baudRate, _viewModel, _dispatcherQueue);

                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.LightGreen);
                StatusText_Input.Text = $"Bağlandı: {portName} ({baudRate})";
                
                LogDebug($"Input port bağlandı: {portName} @ {baudRate}");
            }
            catch (Exception ex)
            {
                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Input.Text = $"Bağlantı Hatası: {ex.Message}";
                LogDebug($"Input port bağlantı hatası: {ex.Message}");
            }
        }

        private async void DisconnectInputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SerialPortManager.Instance.DisconnectAsync();
                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Input.Text = "Giriş Portu Kapalı";
                LogDebug("Input port kapatıldı");
            }
            catch (Exception ex)
            {
                StatusText_Input.Text = $"Kapatma Hatası: {ex.Message}";
                LogDebug($"Input port kapatma hatası: {ex.Message}");
            }
        }

        private async void ConnectOutputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var portName = PortComboBox_Output.SelectedItem as string;
                if (string.IsNullOrEmpty(portName))
                    throw new InvalidOperationException("Çıkış için bir port seçin.");

                // Input port ile aynı port'u seçmeye çalışıyorsa uyar
                var inputPortName = PortComboBox_Input.SelectedItem as string;
                if (!string.IsNullOrEmpty(inputPortName) &&
                    portName == inputPortName &&
                    SerialPortManager.Instance.IsConnected)
                {
                    StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Orange);
                    StatusText_Output.Text = "Output port, Input port ile aynı olamaz. Farklı bir port seçin.";
                    LogDebug($"Port çakışması: Output ve Input için aynı port seçildi: {portName}");
                    return;
                }
                ;

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
                    LogDebug($"Output BaudRate alma hatası: {ex.Message}");
                }
                
                var baudRate = int.Parse(baudRateStr);

                // SerialPortManager ile output port bağla
                await SerialPortManager.Instance.InitializeOutputAsync(portName, baudRate, _viewModel, _dispatcherQueue);

                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.LightGreen);
                StatusText_Output.Text = $"HYI Output Aktif: {portName} ({baudRate})";

                LogDebug($"Output port bağlandı: {portName} @ {baudRate}");
            }
            catch (Exception ex)
            {
                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Output.Text = $"Output Hatası: {ex.Message}";
                LogDebug($"Output port bağlantı hatası: {ex.Message}");
            }
        }

        private async void DisconnectOutputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // SerialPortManager ile output port kapat
                await SerialPortManager.Instance.DisconnectOutputAsync();
                
                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Output.Text = "Çıkış Portu Kapalı";
                LogDebug("Output port kapatıldı");
            }
            catch (Exception ex)
            {
                StatusText_Output.Text = $"Kapatma Hatası: {ex.Message}";
                LogDebug($"Output port kapatma hatası: {ex.Message}");
            }
        }

        // Static accessor methods for backward compatibility with other pages
        public static SerialPortService? GetInputSerialPortService()
        {
            return SerialPortManager.Instance.SerialPortService;
        }

        // Output port accessor method
        public static SerialPortService? GetOutputSerialPortService()
        {
            return SerialPortManager.Instance.OutputPortService;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SerialPortManager'dan event'lere abone ol
            SerialPortManager.Instance.SubscribeToTelemetryData("SettingPage", OnTelemetryDataUpdated);
            
            // Bağlantı durumlarını kontrol et ve UI'ı güncelle
            UpdateConnectionStatus();
            
            LogDebug("SettingPage navigasyonu tamamlandı");
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // SerialPortManager'dan aboneliği iptal et
            SerialPortManager.Instance.UnsubscribeAll("SettingPage");
            
            LogDebug("SettingPage'den ayrıldı - Abonelikler iptal edildi");
        }

        private void UpdateConnectionStatus()
        {
            try
            {
                // Input port durumu
                if (SerialPortManager.Instance.IsConnected)
                {
                    StatusIndicator_Input.Fill = new SolidColorBrush(Colors.LightGreen);
                    StatusText_Input.Text = SerialPortManager.Instance.GetConnectionInfo();
                }
                else
                {
                    StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Red);
                    StatusText_Input.Text = "Giriş Portu Kapalı";
                }

                // Output port durumu
                if (SerialPortManager.Instance.IsOutputConnected)
                {
                    StatusIndicator_Output.Fill = new SolidColorBrush(Colors.LightGreen);
                    StatusText_Output.Text = $"HYI Output Aktif: {SerialPortManager.Instance.GetOutputConnectionInfo()}";
                }
                else
                {
                    StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Red);
                    StatusText_Output.Text = "Çıkış Portu Kapalı";
                }

                LogDebug($"UpdateConnectionStatus: Input={SerialPortManager.Instance.IsConnected}, Output={SerialPortManager.Instance.IsOutputConnected}");
            }
            catch (Exception ex)
            {
                LogDebug($"UpdateConnectionStatus hatası: {ex.Message}");
            }
        }

        private void OnTelemetryDataUpdated(SerialPortService.RocketTelemetryData rocketData)
        {
            // Telemetri verisi alındığında log
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    string rocketInfo = rocketData != null ? $"Roket: {rocketData.RocketAltitude:F1}m" : "Roket: null";
                    
                    LogDebug($"SettingPage: TELEMETRİ GÜNCELLEME - {rocketInfo}");
                }
                catch (Exception ex)
                {
                    LogDebug($"SettingPage: Telemetri güncelleme hatası: {ex.Message}");
                }
            });
        }
        
        // Conditional Debug Logging - RELEASE'de hiç çalışmaz
        [Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}