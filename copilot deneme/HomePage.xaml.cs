using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System.IO.Ports;
using System;
using copilot_deneme.ViewModels;
using System.Globalization;
using System.Text;

namespace copilot_deneme
{
    public sealed partial class HomePage : Page
    {
        private readonly ChartViewModel _viewModel;
        private readonly DispatcherQueue _dispatcherQueue;

        public HomePage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _viewModel = new ChartViewModel();
            this.DataContext = _viewModel;

            // SerialPortService'i yap�land�r
            SerialPortService.ViewModel = _viewModel;
            SerialPortService.Dispatcher = _dispatcherQueue;

            // Serial data received event'i i�in delegate ekle
            SerialPortService.OnDataReceived += OnSerialDataReceived;
            
            System.Diagnostics.Debug.WriteLine("HomePage ba�lat�ld� - Telemetri i�leme sutPage'de ger�ekle�tirilecek");
        }

        private void OnSerialDataReceived(string data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                // Gelen veriyi TextBox'a ekle - sadece ham veri g�sterimi
                SerialDataTextBox.Text += $"{DateTime.Now:HH:mm:ss}: {data}\n";

                // TextBox'� en alta kayd�r
                SerialDataScrollViewer.ChangeView(null, SerialDataScrollViewer.ExtentHeight, null);
                
                // Status g�ncelle
                StatusTextBlock.Text = $"Ham veri al�nd�: {DateTime.Now:HH:mm:ss}";
                
                System.Diagnostics.Debug.WriteLine($"HomePage - Ham veri g�r�nt�lendi: {data}");
            });
        }

        // Temizle butonu click handler'�
        private void ClearData_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            SerialDataTextBox.Text = "";
            StatusTextBlock.Text = "Veriler temizlendi";
            System.Diagnostics.Debug.WriteLine("HomePage verileri temizlendi");
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SerialPortService.OnDataReceived -= OnSerialDataReceived;
            System.Diagnostics.Debug.WriteLine("HomePage'den ayr�ld� - Event handler'lar temizlendi");
        }
    }
}