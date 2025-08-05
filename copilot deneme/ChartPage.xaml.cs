using copilot_deneme.ViewModels;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SkiaSharp;
using System;

namespace copilot_deneme
{
    public sealed partial class ChartPage : Page
    {
        private ChartViewModel? _viewModel;
        private SerialPortService? _serialPortService;

        // ViewModel property for binding
        public ChartViewModel? ViewModel => _viewModel;

        public ChartPage()
        {
            this.InitializeComponent();
            
            // Chart görünüm ayarlarý
            AltitudeSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            SpeedSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            AccelSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            TempSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            PressureySeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            HumiditySeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);

            System.Diagnostics.Debug.WriteLine("ChartPage baþlatýldý - ViewModel baðlantýsý bekleniyor");
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

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SerialPortService'e baðlan ve ViewModel'i al
            ConnectToSerialPortService();
            
            System.Diagnostics.Debug.WriteLine("ChartPage navigasyon tamamlandý");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            System.Diagnostics.Debug.WriteLine("ChartPage'den ayrýldý");
        }
    }
}