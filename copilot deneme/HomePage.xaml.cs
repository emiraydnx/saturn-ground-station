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

            System.Diagnostics.Debug.WriteLine("HomePage ba�lat�ld� - SerialPortService ba�lant�s� bekleniyor");
        }

        private void ConnectToSerialPortService()
        {
            try
            {
                // SettingPage'den SerialPortService instance'�n� al
                _serialPortService = SettingPage.GetInputSerialPortService();
                
                if (_serialPortService != null)
                {
                    // Event handler'� ba�la
                    _serialPortService.OnDataReceived += OnSerialDataReceived;
                    
                    System.Diagnostics.Debug.WriteLine("HomePage SerialPortService'e ba�land�");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("HomePage: SerialPortService instance bulunamad�!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HomePage SerialPortService ba�lant� hatas�: {ex.Message}");
            }
        }

        private void OnSerialDataReceived(string data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Gelen veriyi TextBox'a ekle - sadece ham veri g�sterimi
                    SerialDataTextBox.Text += $"{DateTime.Now:HH:mm:ss}: {data}\n";

                    // TextBox'� en alta kayd�r
                    SerialDataScrollViewer.ChangeView(null, SerialDataScrollViewer.ExtentHeight, null);
                    
                    // Status g�ncelle
                    StatusTextBlock.Text = $"Ham veri al�nd�: {DateTime.Now:HH:mm:ss}";
                    
                    System.Diagnostics.Debug.WriteLine($"HomePage - Ham veri g�r�nt�lendi: {data.Length} karakter");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HomePage veri g�r�nt�leme hatas�: {ex.Message}");
                    StatusTextBlock.Text = $"Veri hatas�: {DateTime.Now:HH:mm:ss}";
                }
            });
        }

        // Temizle butonu click handler'�
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
                System.Diagnostics.Debug.WriteLine($"HomePage veri temizleme hatas�: {ex.Message}");
                StatusTextBlock.Text = "Temizleme hatas�";
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SerialPortService'e ba�lan
            ConnectToSerialPortService();
            
            System.Diagnostics.Debug.WriteLine("HomePage navigasyon tamamland�");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // Event handler'lar� kald�r
            if (_serialPortService != null)
            {
                _serialPortService.OnDataReceived -= OnSerialDataReceived;
            }
            
            System.Diagnostics.Debug.WriteLine("HomePage'den ayr�ld� - Event handler'lar temizlendi");
        }
    }
}