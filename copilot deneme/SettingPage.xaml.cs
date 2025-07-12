using System.IO.Ports;
using copilot_deneme.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Linq;
using Windows.UI; // This is already present but Colors class needs to be accessed differently

namespace copilot_deneme
{
    public sealed partial class SettingPage : Page
    {
        private ChartViewModel _viewModel;
        private DispatcherQueue _dispatcherQueue;
        
        public SettingPage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            
            var viewModel = new ChartViewModel();
            this.DataContext = viewModel;
            _viewModel = viewModel; // Ayn� instance'� kullan
            
            SerialPortService.ViewModel = _viewModel;
            SerialPortService.Dispatcher = _dispatcherQueue;
            
            // �lk y�klemede portlar� doldur
            RefreshAvailablePorts();
            BaudRateComboBox.SelectedIndex = 0; 
            
            // Sayfa y�klendi�inde durumu g�ncelle
            UpdateConnectionStatus();
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            RefreshAvailablePorts();
            
            // Kullan�c�ya geri bildirim ver
            _dispatcherQueue.TryEnqueue(() =>
            {
                StatusText.Text = "Portlar yenilendi";
                StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 144, 238, 144)); // LightGreen
            });
            
            System.Diagnostics.Debug.WriteLine("Ports refreshed");
        }
        
        private void RefreshAvailablePorts()
        {
            try
            {
                var availablePorts = SerialPort.GetPortNames();
                var currentSelection = PortComboBox.SelectedItem as string;
                
                _dispatcherQueue.TryEnqueue(() =>
                {
                    PortComboBox.ItemsSource = availablePorts;
                    
                    // E�er �nceden se�ili bir port varsa ve hala mevcutsa, onu tekrar se�
                    if (!string.IsNullOrEmpty(currentSelection) && availablePorts.Contains(currentSelection))
                    {
                        PortComboBox.SelectedItem = currentSelection;
                    }
                    else if (availablePorts.Length > 0)
                    {
                        PortComboBox.SelectedIndex = 0; // �lk portu se�
                    }
                    
                    // Port say�s�n� g�ster
                    StatusText.Text = $"{availablePorts.Length} port bulundu";
                    StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 173, 216, 230)); // LightBlue
                });
                
                System.Diagnostics.Debug.WriteLine($"Found {availablePorts.Length} ports: {string.Join(", ", availablePorts)}");
            }
            catch (System.Exception ex)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText.Text = $"Port tarama hatas�: {ex.Message}";
                        StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)); // Red
                });
                
                System.Diagnostics.Debug.WriteLine($"Error refreshing ports: {ex.Message}");
            }
        }

        private void OpenPort_Click(object sender, RoutedEventArgs e)
        {
            var portName = PortComboBox.SelectedItem as string;
            var baudRateItem = BaudRateComboBox.SelectedItem as ComboBoxItem;
            var baudRate = int.Parse(baudRateItem?.Content.ToString() ?? "115200");

            if (string.IsNullOrEmpty(portName))
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText.Text = "L�tfen bir port se�in";
                    StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 165, 0)); // Orange
                });
                return;
            }

            try
            {
                // �nce mevcut ba�lant�y� kapat
                if (SerialPortService.SerialPort != null && SerialPortService.SerialPort.IsOpen)
                {
                    SerialPortService.StopReading();
                }

                // Yeni port yap�land�rmas�
                SerialPortService.Initialize(portName, baudRate);
                SerialPortService.StartReading();

                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText.Text = $"Ba�land�: {portName} ({baudRate} baud)";
                    StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 144, 238, 144)); // LightGreen
                    UpdateConnectionStatus();
                });
                
                System.Diagnostics.Debug.WriteLine($"Port opened successfully: {portName} at {baudRate} baud");
            }
            catch (System.Exception ex)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText.Text = $"Ba�lant� hatas�: {ex.Message}";
                    StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)); // Red
                    UpdateConnectionStatus();
                });
                System.Diagnostics.Debug.WriteLine($"Error opening port: {ex.Message}");
            }
        }
        
        private void ClosePort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SerialPortService.StopReading();
                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText.Text = "Ba�lant� kapat�ld�";
                    StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 165, 0)); // Orange
                    UpdateConnectionStatus();
                });
                
                System.Diagnostics.Debug.WriteLine("Port closed successfully");
            }
            catch (System.Exception ex)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText.Text = $"Kapatma hatas�: {ex.Message}";
                    StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)); // Red
                });
                System.Diagnostics.Debug.WriteLine($"Error closing port: {ex.Message}");
            }
        }
        
        private void UpdateConnectionStatus()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                bool isConnected = SerialPortService.IsPortOpen();
                
                if (isConnected)
                {
                    StatusIndicator.Fill = new SolidColorBrush(Color.FromArgb(255, 144, 238, 144)); // LightGreen
                    if (StatusText.Text == "Port ba�lant�s� kapal�")
                    {
                        StatusText.Text = $"Ba�l�: {SerialPortService.SerialPort?.PortName}";
                        StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 144, 238, 144)); // LightGreen
                    }
                }
                else
                {
                    StatusIndicator.Fill = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)); // Red
                    if (!StatusText.Text.Contains("hatas�") && !StatusText.Text.Contains("yenilendi"))
                    {
                        StatusText.Text = "Port ba�lant�s� kapal�";
                        StatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)); // Gray
                    }
                }
            });
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            // Sayfa her a��ld���nda durum g�ncelle
            UpdateConnectionStatus();
            RefreshAvailablePorts();
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // Cleanup if needed
        }
    }
}