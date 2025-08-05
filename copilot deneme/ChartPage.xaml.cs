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
            
            // Chart g�r�n�m ayarlar�
            AltitudeSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            SpeedSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            AccelSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            TempSeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            PressureySeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);
            HumiditySeries.LegendTextPaint = new SolidColorPaint(SKColors.White, 10);

            System.Diagnostics.Debug.WriteLine("ChartPage ba�lat�ld� - ViewModel ba�lant�s� bekleniyor");
        }

        private void ConnectToSerialPortService()
        {
            try
            {
                // SettingPage'den SerialPortService instance'�n� al
                _serialPortService = SettingPage.GetInputSerialPortService();
                
                if (_serialPortService != null)
                {
                    // ViewModel'i SerialPortService'den al
                    _viewModel = _serialPortService.ViewModel;
                    
                    if (_viewModel != null)
                    {
                        this.DataContext = _viewModel;
                        System.Diagnostics.Debug.WriteLine("ChartPage ViewModel'e ba�land�");
                    }
                    else
                    {
                        // Yeni ViewModel olu�tur
                        _viewModel = new ChartViewModel();
                        this.DataContext = _viewModel;
                        _serialPortService.ViewModel = _viewModel;
                        System.Diagnostics.Debug.WriteLine("ChartPage yeni ViewModel olu�turdu");
                    }
                }
                else
                {
                    // SerialPortService yoksa kendi ViewModel'imizi olu�tur
                    _viewModel = new ChartViewModel();
                    this.DataContext = _viewModel;
                    System.Diagnostics.Debug.WriteLine("ChartPage: SerialPortService bulunamad�, ba��ms�z ViewModel olu�turuldu");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ChartPage ViewModel ba�lant� hatas�: {ex.Message}");
                
                // Hata durumunda varsay�lan ViewModel olu�tur
                _viewModel = new ChartViewModel();
                this.DataContext = _viewModel;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SerialPortService'e ba�lan ve ViewModel'i al
            ConnectToSerialPortService();
            
            System.Diagnostics.Debug.WriteLine("ChartPage navigasyon tamamland�");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            System.Diagnostics.Debug.WriteLine("ChartPage'den ayr�ld�");
        }
    }
}