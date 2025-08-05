using copilot_deneme.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.Logging;
using System;
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

            // Event handler'ları kaydet
            _serialPortService.OnHYIPacketReceived += OnHYIPacketReceived;
            _serialPortService.OnError += OnSerialPortError;
            _outputPortService.OnError += OnOutputPortError;
            
            // İlk yüklemede portları doldur
            RefreshAvailablePorts();
            BaudRateComboBox_Input.SelectedIndex = 0;
            BaudRateComboBox_Output.SelectedIndex = 0;
            
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
                
                var baudRate = int.Parse((BaudRateComboBox_Input.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "9600");

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

                var baudRate = int.Parse((BaudRateComboBox_Output.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "9600");

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
            System.Diagnostics.Debug.WriteLine("SettingPage navigasyonu tamamlandı");
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // Event handler'ları kaldır
            _serialPortService.OnHYIPacketReceived -= OnHYIPacketReceived;
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
                System.Diagnostics.Debug.WriteLine("SettingPage SerialPortService instance'ları dispose edildi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage dispose hatası: {ex.Message}");
            }
        }
    }
}