using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System;
using copilot_deneme.ViewModels;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace copilot_deneme
{
    public sealed partial class HomePage : Page
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private SerialPortService? _serialPortService;

        // Performans i�in ObservableCollection ve ListView kullan�l�yor
        public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();
        private const int MaxLogEntries = 500; // Bellek kullan�m�n� kontrol alt�nda tutmak i�in s�n�r

        public HomePage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            this.DataContext = this; // DataContext'i sayfan�n kendisine ayarlayarak LogEntries'e binding yapmay� sa�l�yoruz

            Debug.WriteLine("HomePage ba�lat�ld� - SerialPortService ba�lant�s� bekleniyor");
        }

        private void ConnectToSerialPortService()
        {
            try
            {
                // SerialPortManager'dan SerialPortService instance'�n� al
                _serialPortService = SerialPortManager.Instance.SerialPortService;
                
                if (_serialPortService != null)
                {
                    Debug.WriteLine("HomePage SerialPortManager'a ba�land�");
                }
                else
                {
                    Debug.WriteLine("HomePage: SerialPortService instance bulunamad�!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HomePage SerialPortManager ba�lant� hatas�: {ex.Message}");
            }
        }

        private void OnSerialDataReceived(string data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Verimsiz string birle�tirme yerine koleksiyona ekle (O(1) operasyon)
                    LogEntries.Add($"{DateTime.Now:HH:mm:ss}: {data}");

                    // Koleksiyonun a��r� b�y�mesini engelle
                    if (LogEntries.Count > MaxLogEntries)
                    {
                        LogEntries.RemoveAt(0);
                    }

                    // Yeni eklenen ��eyi g�r�n�r k�l
                    if (LogListView.Items.Count > 0)
                    {
                        LogListView.ScrollIntoView(LogListView.Items[LogEntries.Count - 1]);
                    }
                    
                    // Status g�ncelle
                    StatusTextBlock.Text = $"Ham veri al�nd�: {DateTime.Now:HH:mm:ss}";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"HomePage veri g�r�nt�leme hatas�: {ex.Message}");
                    StatusTextBlock.Text = $"Veri hatas�: {DateTime.Now:HH:mm:ss}";
                }
            });
        }

        // Temizle butonu click handler'�
        private void ClearData_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                LogEntries.Clear(); // Koleksiyonu temizle
                StatusTextBlock.Text = "Veriler temizlendi";
                Debug.WriteLine("HomePage verileri temizlendi");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HomePage veri temizleme hatas�: {ex.Message}");
                StatusTextBlock.Text = "Temizleme hatas�";
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SerialPortManager'a abone ol
            SerialPortManager.Instance.SubscribeToDataReceived("HomePage", OnSerialDataReceived);
            
            // SerialPortService'e ba�lan
            ConnectToSerialPortService();
            
            Debug.WriteLine("HomePage navigasyon tamamland�");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // SerialPortManager'dan aboneli�i iptal et
            SerialPortManager.Instance.UnsubscribeAll("HomePage");
            
            Debug.WriteLine("HomePage'den ayr�ld� - Abonelik iptal edildi");
        }
    }
}