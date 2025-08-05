using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System;
using copilot_deneme.ViewModels;
using Microsoft.UI.Xaml.Navigation;

namespace copilot_deneme
{
    public sealed partial class HomePage : Page
    {
        private readonly ChartViewModel _viewModel;
        private readonly DispatcherQueue _dispatcherQueue;
        private SerialPortService? _serialPortService;

        public HomePage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _viewModel = new ChartViewModel();
            this.DataContext = _viewModel;

            System.Diagnostics.Debug.WriteLine("HomePage baþlatýldý - SerialPortService baðlantýsý bekleniyor");
        }

        private void ConnectToSerialPortService()
        {
            try
            {
                // SettingPage'den SerialPortService instance'ýný al
                _serialPortService = SettingPage.GetInputSerialPortService();
                
                if (_serialPortService != null)
                {
                    // Event handler'ý baðla
                    _serialPortService.OnDataReceived += OnSerialDataReceived;
                    
                    System.Diagnostics.Debug.WriteLine("HomePage SerialPortService'e baðlandý");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("HomePage: SerialPortService instance bulunamadý!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HomePage SerialPortService baðlantý hatasý: {ex.Message}");
            }
        }

        private void OnSerialDataReceived(string data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Gelen veriyi TextBox'a ekle - sadece ham veri gösterimi
                    SerialDataTextBox.Text += $"{DateTime.Now:HH:mm:ss}: {data}\n";

                    // TextBox'ý en alta kaydýr
                    SerialDataScrollViewer.ChangeView(null, SerialDataScrollViewer.ExtentHeight, null);
                    
                    // Status güncelle
                    StatusTextBlock.Text = $"Ham veri alýndý: {DateTime.Now:HH:mm:ss}";
                    
                    System.Diagnostics.Debug.WriteLine($"HomePage - Ham veri görüntülendi: {data.Length} karakter");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HomePage veri görüntüleme hatasý: {ex.Message}");
                    StatusTextBlock.Text = $"Veri hatasý: {DateTime.Now:HH:mm:ss}";
                }
            });
        }

        // Temizle butonu click handler'ý
        private void ClearData_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                SerialDataTextBox.Text = "";
                StatusTextBlock.Text = "Veriler temizlendi";
                System.Diagnostics.Debug.WriteLine("HomePage verileri temizlendi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HomePage veri temizleme hatasý: {ex.Message}");
                StatusTextBlock.Text = "Temizleme hatasý";
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SerialPortService'e baðlan
            ConnectToSerialPortService();
            
            System.Diagnostics.Debug.WriteLine("HomePage navigasyon tamamlandý");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // Event handler'larý kaldýr
            if (_serialPortService != null)
            {
                _serialPortService.OnDataReceived -= OnSerialDataReceived;
            }
            
            System.Diagnostics.Debug.WriteLine("HomePage'den ayrýldý - Event handler'lar temizlendi");
        }
    }
}