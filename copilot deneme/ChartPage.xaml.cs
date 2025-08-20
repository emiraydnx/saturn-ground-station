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
            LogDebug("ChartPage ba�lat�ld�");
        }

        private void ConfigureChartPerformance()
        {
            try
            {
                var legendPaint = new SolidColorPaint(SKColors.White, 10);

                // T�m chart'lara performans ayarlar�
                AltitudeSeries.LegendTextPaint = legendPaint;
                SpeedSeries.LegendTextPaint = legendPaint;
                AccelSeries.LegendTextPaint = legendPaint;
                TempSeries.LegendTextPaint = legendPaint;
                PressureySeries.LegendTextPaint = legendPaint;
                HumiditySeries.LegendTextPaint = legendPaint;

                // Animation h�zland�r
                AltitudeSeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                SpeedSeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                AccelSeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                TempSeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                PressureySeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
                HumiditySeries.AnimationsSpeed = TimeSpan.FromMilliseconds(100);

                LogDebug("Chart performans ayarlar� uyguland�");
            }
            catch (Exception ex)
            {
                LogDebug($"Chart performans ayarlar� hatas�: {ex.Message}");
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
                    LogDebug("ChartPage mevcut ViewModel'e ba�land�");
                }
                else
                {
                    _viewModel = new ChartViewModel();
                    if (_serialPortService != null)
                        _serialPortService.ViewModel = _viewModel;
                    LogDebug("ChartPage yeni ViewModel olu�turdu");
                }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    this.DataContext = _viewModel;
                    LogDebug("ChartPage DataContext ayarland�");
                });
            }
            catch (Exception ex)
            {
                LogDebug($"ViewModel ba�lant� hatas�: {ex.Message}");
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

            LogDebug("ChartPage navigasyon tamamland�");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SerialPortManager.Instance.UnsubscribeAll("ChartPage");
            LogDebug("ChartPage'den ayr�ld�");
        }

        private void OnTelemetryDataReceived(SerialPortService.RocketTelemetryData data)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (_viewModel != null && data != null)
                    {
                        // Batch update i�in yeni metod kullan
                        _viewModel.BatchUpdateChartData(data);
                        LogDebugChart(data.RocketAltitude);
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Chart g�ncellemesi hatas�: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"[ChartPage] Chart g�ncellendi - �rtifa: {altitude:F1}m");
        }
    }
}