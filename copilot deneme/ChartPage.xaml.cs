using copilot_deneme.ViewModels;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SkiaSharp;
using System;
using Microsoft.UI.Dispatching;
using System.Threading;
using System.Threading.Tasks;

namespace copilot_deneme
{
    public sealed partial class ChartPage : Page
    {
        private ChartViewModel? _viewModel;
        private SerialPortService? _serialPortService;
        private readonly DispatcherQueue _dispatcherQueue;
        private CancellationTokenSource? _gpsUpdateCancellationTokenSource;
        private readonly Random _random = new Random();

        // GPS koordinat base deðerleri
        private const double BASE_ROCKET_LATITUDE = 38.5360;
        private const double BASE_ROCKET_LONGITUDE = 27.022;
        private const double BASE_PAYLOAD_LATITUDE = 38.5360;
        private const double BASE_PAYLOAD_LONGITUDE = 27.022;
        private const double GPS_VARIATION_RANGE = 0.05; // ±0.05 deðiþim aralýðý

        // ViewModel property for binding
        public ChartViewModel? ViewModel => _viewModel;

        public ChartPage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            
            // Chart görünüm ayarlarý
            AltitudeSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            SpeedSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            AccelSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            TempSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            PressureySeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            HumiditySeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);

            System.Diagnostics.Debug.WriteLine("ChartPage baþlatýldý - ViewModel baðlantýsý ve GPS koordinat güncellemeleri bekleniyor");
        }

        /// <summary>
        /// GPS koordinatlarýný düzenli olarak güncelleyen background task'ý baþlatýr
        /// </summary>
        private async Task StartGpsUpdateTaskAsync()
        {
            try
            {
                _gpsUpdateCancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _gpsUpdateCancellationTokenSource.Token;

                // Ýlk deðerleri hemen ayarla
                UpdateGpsCoordinates();

                // Background task baþlat
                _ = Task.Run(async () =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(2000, cancellationToken); // 2 saniye bekle
                            if (!cancellationToken.IsCancellationRequested)
                            {
                                _dispatcherQueue.TryEnqueue(() => UpdateGpsCoordinates());
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"GPS güncelleme task hatasý: {ex.Message}");
                        }
                    }
                }, cancellationToken);

                System.Diagnostics.Debug.WriteLine("GPS koordinat güncelleme task'ý baþlatýldý - 2 saniye aralýklarla güncelleme yapýlacak");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPS task baþlatma hatasý: {ex.Message}");
            }
        }

        /// <summary>
        /// GPS koordinatlarýný belirtilen base deðerlerin ±0.05 aralýðýnda rastgele günceller
        /// </summary>
        private void UpdateGpsCoordinates()
        {
            try
            {
                // Roket GPS koordinatlarý - Base deðerlerin ±0.05 aralýðýnda rastgele deðiþim
                double rocketLatitude = BASE_ROCKET_LATITUDE + (_random.NextDouble() - 0.5) * 2 * GPS_VARIATION_RANGE;
                double rocketLongitude = BASE_ROCKET_LONGITUDE + (_random.NextDouble() - 0.5) * 2 * GPS_VARIATION_RANGE;

                // Payload GPS koordinatlarý - Base deðerlerin ±0.05 aralýðýnda rastgele deðiþim
                double payloadLatitude = BASE_PAYLOAD_LATITUDE + (_random.NextDouble() - 0.5) * 2 * GPS_VARIATION_RANGE;
                double payloadLongitude = BASE_PAYLOAD_LONGITUDE + (_random.NextDouble() - 0.5) * 2 * GPS_VARIATION_RANGE;

                // TextBox'larý güncelle - 6 basamak hassasiyet ile
                if (RocketLatitudeTextBox != null)
                    RocketLatitudeTextBox.Text = rocketLatitude.ToString("F6");
                if (RocketLongitudeTextBox != null)
                    RocketLongitudeTextBox.Text = rocketLongitude.ToString("F6");
                if (PayloadLatitudeTextBox != null)
                    PayloadLatitudeTextBox.Text = payloadLatitude.ToString("F6");
                if (PayloadLongitudeTextBox != null)
                    PayloadLongitudeTextBox.Text = payloadLongitude.ToString("F6");

                System.Diagnostics.Debug.WriteLine($"?? ChartPage GPS koordinatlarý güncellendi:");
                System.Diagnostics.Debug.WriteLine($"   ?? Roket: {rocketLatitude:F6}, {rocketLongitude:F6}");
                System.Diagnostics.Debug.WriteLine($"   ?? Payload: {payloadLatitude:F6}, {payloadLongitude:F6}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPS koordinat güncelleme hatasý: {ex.Message}");
            }
        }

        private void ConnectToSerialPortService()
        {
            try
            {
                // SettingPage'den SerialPortService instance'ýný al
                _serialPortService = SettingPage.GetInputSerialPortService();
                
                if (_serialPortService != null)
                {
                    // ViewModel'i SerialPortService'den al
                    _viewModel = _serialPortService.ViewModel;
                    
                    if (_viewModel != null)
                    {
                        this.DataContext = _viewModel;
                        System.Diagnostics.Debug.WriteLine("ChartPage ViewModel'e baðlandý");
                    }
                    else
                    {
                        // Yeni ViewModel oluþtur
                        _viewModel = new ChartViewModel();
                        this.DataContext = _viewModel;
                        _serialPortService.ViewModel = _viewModel;
                        System.Diagnostics.Debug.WriteLine("ChartPage yeni ViewModel oluþturdu");
                    }
                }
                else
                {
                    // SerialPortService yoksa kendi ViewModel'imizi oluþtur
                    _viewModel = new ChartViewModel();
                    this.DataContext = _viewModel;
                    System.Diagnostics.Debug.WriteLine("ChartPage: SerialPortService bulunamadý, baðýmsýz ViewModel oluþturuldu");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ChartPage ViewModel baðlantý hatasý: {ex.Message}");
                
                // Hata durumunda varsayýlan ViewModel oluþtur
                _viewModel = new ChartViewModel();
                this.DataContext = _viewModel;
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SerialPortService'e baðlan ve ViewModel'i al
            ConnectToSerialPortService();
            
            // GPS koordinat güncelleme task'ýný baþlat
            await StartGpsUpdateTaskAsync();
            
            System.Diagnostics.Debug.WriteLine("ChartPage navigasyon tamamlandý - GPS koordinat güncellemeleri aktif");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // GPS güncelleme task'ýný durdur
            if (_gpsUpdateCancellationTokenSource is not null)
            {
                _gpsUpdateCancellationTokenSource.Cancel();
                _gpsUpdateCancellationTokenSource.Dispose();
                _gpsUpdateCancellationTokenSource = null;
                System.Diagnostics.Debug.WriteLine("GPS güncelleme task'ý durduruldu");
            }
            
            System.Diagnostics.Debug.WriteLine("ChartPage'den ayrýldý");
        }
    }
}