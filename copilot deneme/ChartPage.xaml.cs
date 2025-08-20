using copilot_deneme.ViewModels;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SkiaSharp;
using System;
using System.Diagnostics;

namespace copilot_deneme
{
    public sealed partial class ChartPage : Page
    {
        private ChartViewModel? _viewModel;
        private SerialPortService? _serialPortService;
        private readonly DispatcherQueue _dispatcherQueue;

        public ChartViewModel? ViewModel => _viewModel;

        public ChartPage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            ConfigureChartPerformance();
            LogDebug("ChartPage baþlatýldý");
        }

        private void ConfigureChartPerformance()
        {
            try
            {
                var legendPaint = new SolidColorPaint(SKColors.White, 10);

                // Tüm chart'lara performans ayarlarý
                AltitudeSeries.LegendTextPaint = legendPaint;
                SpeedSeries.LegendTextPaint = legendPaint;
                AccelSeries.LegendTextPaint = legendPaint;
                TempSeries.LegendTextPaint = legendPaint;
                PressureySeries.LegendTextPaint = legendPaint;
                HumiditySeries.LegendTextPaint = legendPaint;

                // Animation hýzlandýr
                AltitudeSeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                SpeedSeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                AccelSeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                TempSeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                PressureySeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                HumiditySeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);

                LogDebug("Chart performans ayarlarý uygulandý");
            }
            catch (Exception ex)
            {
                LogDebug($"Chart performans ayarlarý hatasý: {ex.Message}");
            }
        }

        private void ConnectToSerialPortService()
        {
            try
            {
                _serialPortService = SerialPortManager.Instance.SerialPortService;

                if (_serialPortService?.ViewModel != null)
                {
                    _viewModel = _serialPortService.ViewModel;
                    LogDebug("ChartPage mevcut ViewModel'e baðlandý");
                }
                else
                {
                    _viewModel = new ChartViewModel();
                    if (_serialPortService != null)
                        _serialPortService.ViewModel = _viewModel;
                    LogDebug("ChartPage yeni ViewModel oluþturdu");
                }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    this.DataContext = _viewModel;
                    LogDebug("ChartPage DataContext ayarlandý");
                });
            }
            catch (Exception ex)
            {
                LogDebug($"ViewModel baðlantý hatasý: {ex.Message}");
                _viewModel = new ChartViewModel();
                _dispatcherQueue.TryEnqueue(() =>
                {
                    this.DataContext = _viewModel;
                });
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            SerialPortManager.Instance.SubscribeToTelemetryData("ChartPage", OnTelemetryDataReceived);
            ConnectToSerialPortService();

            LogDebug("ChartPage navigasyon tamamlandý");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SerialPortManager.Instance.UnsubscribeAll("ChartPage");
            LogDebug("ChartPage'den ayrýldý");
        }

        private void OnTelemetryDataReceived(SerialPortService.RocketTelemetryData data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (_viewModel != null && data != null)
                    {
                        // Batch update için yeni metod kullan
                        _viewModel.BatchUpdateChartData(data);
                        LogDebugChart(data.RocketAltitude);
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Chart güncellemesi hatasý: {ex.Message}");
                }
            });
        }

        [Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ChartPage] {message}");
        }

        [Conditional("DEBUG")]
        private static void LogDebugChart(float altitude)
        {
            System.Diagnostics.Debug.WriteLine($"[ChartPage] Chart güncellendi - Ýrtifa: {altitude:F1}m");
        }
    }
}