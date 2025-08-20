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

        // Performans için ObservableCollection ve ListView kullanýlýyor
        public ObservableCollection<string> LogEntries { get; } = new ObservableCollection<string>();
        private const int MaxLogEntries = 500; // Bellek kullanýmýný kontrol altýnda tutmak için sýnýr

        public HomePage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            this.DataContext = this; // DataContext'i sayfanýn kendisine ayarlayarak LogEntries'e binding yapmayý saðlýyoruz

            Debug.WriteLine("HomePage baþlatýldý - SerialPortService baðlantýsý bekleniyor");
        }

        private void ConnectToSerialPortService()
        {
            try
            {
                // SerialPortManager'dan SerialPortService instance'ýný al
                _serialPortService = SerialPortManager.Instance.SerialPortService;
                
                if (_serialPortService != null)
                {
                    Debug.WriteLine("HomePage SerialPortManager'a baðlandý");
                }
                else
                {
                    Debug.WriteLine("HomePage: SerialPortService instance bulunamadý!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HomePage SerialPortManager baðlantý hatasý: {ex.Message}");
            }
        }

        private void OnSerialDataReceived(string data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Verimsiz string birleþtirme yerine koleksiyona ekle (O(1) operasyon)
                    LogEntries.Add($"{DateTime.Now:HH:mm:ss}: {data}");

                    // Koleksiyonun aþýrý büyümesini engelle
                    if (LogEntries.Count > MaxLogEntries)
                    {
                        LogEntries.RemoveAt(0);
                    }

                    // Yeni eklenen öðeyi görünür kýl
                    if (LogListView.Items.Count > 0)
                    {
                        LogListView.ScrollIntoView(LogListView.Items[LogEntries.Count - 1]);
                    }
                    
                    // Status güncelle
                    StatusTextBlock.Text = $"Ham veri alýndý: {DateTime.Now:HH:mm:ss}";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"HomePage veri görüntüleme hatasý: {ex.Message}");
                    StatusTextBlock.Text = $"Veri hatasý: {DateTime.Now:HH:mm:ss}";
                }
            });
        }

        // Temizle butonu click handler'ý
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
                Debug.WriteLine($"HomePage veri temizleme hatasý: {ex.Message}");
                StatusTextBlock.Text = "Temizleme hatasý";
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SerialPortManager'a abone ol
            SerialPortManager.Instance.SubscribeToDataReceived("HomePage", OnSerialDataReceived);
            
            // SerialPortService'e baðlan
            ConnectToSerialPortService();
            
            Debug.WriteLine("HomePage navigasyon tamamlandý");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // SerialPortManager'dan aboneliði iptal et
            SerialPortManager.Instance.UnsubscribeAll("HomePage");
            
            Debug.WriteLine("HomePage'den ayrýldý - Abonelik iptal edildi");
        }
    }
}