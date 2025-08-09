using copilot_deneme.TelemetryData;
using copilot_deneme.Interfaces;
using copilot_deneme.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.Extensions.Logging;
using System;
using static copilot_deneme.TelemetryData.TelemetryConstants;

namespace copilot_deneme.Services
{
    /// <summary>
    /// Chart güncelleme servisi - ViewModel ile iletiþim kurar ve chart'larý günceller
    /// SerialPortService'den ayrýlan chart güncelleme sorumluluðu
    /// </summary>
    public class ChartUpdateService : IChartUpdateService
    {
        private readonly ILogger<ChartUpdateService>? _logger;
        private readonly Random _random = new Random();

        #region Properties
        public ChartViewModel? ViewModel { get; set; }
        public DispatcherQueue? Dispatcher { get; set; }
        #endregion

        #region Constructor
        public ChartUpdateService(ILogger? logger = null)
        {
            _logger = logger as ILogger<ChartUpdateService>;
        }
        #endregion

        #region Chart Update Methods
        /// <summary>
        /// Roket verilerini chart'lara ekler
        /// </summary>
        public void UpdateChartsFromRocket(RocketTelemetryData rocketData)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                    {
                        _logger?.LogWarning("UpdateChartsFromRocket: ViewModel is null!");
                        return;
                    }

                    // SADECE ROKET VERÝLERÝNÝ CHART'A EKLE
                    ViewModel.AddRocketAltitudeValue(rocketData.RocketAltitude);
                    ViewModel.addRocketAccelXValue(rocketData.AccelX);
                    ViewModel.addRocketAccelYValue(rocketData.AccelY);
                    ViewModel.addRocketAccelZValue(rocketData.AccelZ);
                    ViewModel.addRocketSpeedValue(rocketData.RocketSpeed);
                    ViewModel.addRocketTempValue(rocketData.RocketTemperature);
                    ViewModel.addRocketPressureValue(rocketData.RocketPressure);

                    ViewModel.UpdateStatus($"Roket verisi: {DateTime.Now:HH:mm:ss} - Paket: #{rocketData.PacketCounter}");

                    _logger?.LogDebug("Roket chart'larý güncellendi - Ýrtifa: {Altitude:F2}m", rocketData.RocketAltitude);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Roket chart güncelleme hatasý");
                }
            });
        }

        /// <summary>
        /// Payload verilerini chart'lara ekler
        /// </summary>
        public void UpdateChartsFromPayload(PayloadTelemetryData payloadData)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                    {
                        _logger?.LogWarning("UpdateChartsFromPayload: ViewModel is null!");
                        return;
                    }

                    // SADECE PAYLOAD VERÝLERÝNÝ CHART'A EKLE
                    ViewModel.addPayloadAltitudeValue(payloadData.PayloadAltitude);
                    ViewModel.addPayloadSpeedValue(payloadData.PayloadSpeed);
                    ViewModel.addPayloadTempValue(payloadData.PayloadTemperature);
                    ViewModel.addPayloadPressureValue(payloadData.PayloadPressure);
                    ViewModel.addPayloadHumidityValue(payloadData.PayloadHumidity);

                    ViewModel.UpdateStatus($"Payload verisi: {DateTime.Now:HH:mm:ss} - Paket: #{payloadData.PacketCounter}");

                    _logger?.LogDebug("Payload chart'larý güncellendi - Ýrtifa: {Altitude:F2}m", payloadData.PayloadAltitude);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Payload chart güncelleme hatasý");
                }
            });
        }

        /// <summary>
        /// HYIDenem verilerini chart'lara ekler
        /// </summary>
        public void UpdateChartsFromHYIDenem(HYIDenemeData hyiDenemeData)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                    {
                        _logger?.LogWarning("UpdateChartsFromHYIDenem: ViewModel is null!");
                        return;
                    }

                    // ROKET VERÝLERÝNÝN HEPSÝNÝ CHART'A EKLE
                    ViewModel.AddRocketAltitudeValue(hyiDenemeData.RocketAltitude);
                    ViewModel.addRocketAccelXValue(hyiDenemeData.AccelX);
                    ViewModel.addRocketAccelYValue(hyiDenemeData.AccelY);
                    ViewModel.addRocketAccelZValue(hyiDenemeData.AccelZ);
                    ViewModel.addRocketSpeedValue(hyiDenemeData.RocketSpeed);
                    ViewModel.addRocketTempValue(hyiDenemeData.RocketTemperature);
                    ViewModel.addRocketPressureValue(hyiDenemeData.RocketPressure);

                    // PAYLOAD VERÝLERÝNÝN HEPSÝNÝ CHART'A EKLE
                    ViewModel.addPayloadAltitudeValue(hyiDenemeData.PayloadAltitude);
                    ViewModel.addPayloadSpeedValue(hyiDenemeData.PayloadSpeed);
                    ViewModel.addPayloadTempValue(hyiDenemeData.PayloadTemperature);
                    ViewModel.addPayloadPressureValue(hyiDenemeData.PayloadPressure);
                    ViewModel.addPayloadHumidityValue(hyiDenemeData.PayloadHumidity);

                    ViewModel.UpdateStatus($"HYIDenem verisi: {DateTime.Now:HH:mm:ss} - #{hyiDenemeData.PacketCounter}");

                    _logger?.LogDebug("HYIDenem chart'larý güncellendi - Roket: {RocketAlt:F2}m, Payload: {PayloadAlt:F2}m", 
                        hyiDenemeData.RocketAltitude, hyiDenemeData.PayloadAltitude);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "HYIDenem chart güncelleme hatasý");
                }
            });
        }

        /// <summary>
        /// Dýþ kaynak verilerini chart'lara ekler
        /// </summary>
        public void UpdateChartsFromExternalData(
            float rocketAltitude, float payloadAltitude,
            float accelX, float accelY, float accelZ, 
            float rocketSpeed, float payloadSpeed,
            float rocketTemp, float payloadTemp, 
            float rocketPressure, float payloadPressure,
            float payloadHumidity, string source = "External")
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                    {
                        _logger?.LogWarning("{Source}: ViewModel is null!", source);
                        return;
                    }

                    UpdateViewModelData(rocketAltitude, payloadAltitude,
                        rocketSpeed, payloadSpeed, rocketTemp, payloadTemp, rocketPressure,
                        payloadPressure, payloadHumidity, accelX, accelY, accelZ);

                    ViewModel.UpdateStatus($"{source} verisi: {DateTime.Now:HH:mm:ss}");

                    _logger?.LogDebug("{Source} tüm veriler chart'lara eklendi", source);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "{Source} chart güncelleme hatasý", source);
                }
            });
        }

        /// <summary>
        /// Chart durumunu günceller
        /// </summary>
        public void UpdateStatus(string status)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    ViewModel?.UpdateStatus(status);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Status güncelleme hatasý");
                }
            });
        }
        #endregion

        #region Combined Chart Updates
        /// <summary>
        /// Roket ve Payload verilerini birlikte chart'lara ekler
        /// </summary>
        public void UpdateChartsFromBothSources(RocketTelemetryData? rocketData, PayloadTelemetryData? payloadData)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel == null)
                    {
                        _logger?.LogWarning("UpdateChartsFromBothSources: ViewModel is null!");
                        return;
                    }

                    // ROKET VERÝLERÝNi SADECE GERÇEK VERÝ VARSA EKLE
                    if (rocketData != null)
                    {
                        ViewModel.AddRocketAltitudeValue(rocketData.RocketAltitude);
                        ViewModel.addRocketAccelXValue(rocketData.AccelX);
                        ViewModel.addRocketAccelYValue(rocketData.AccelY);
                        ViewModel.addRocketAccelZValue(rocketData.AccelZ);
                        ViewModel.addRocketSpeedValue(rocketData.RocketSpeed);
                        ViewModel.addRocketTempValue(rocketData.RocketTemperature);
                        ViewModel.addRocketPressureValue(rocketData.RocketPressure);
                    }

                    // PAYLOAD VERÝLERÝNi SADECE GERÇEK VERÝ VARSA EKLE
                    if (payloadData != null)
                    {
                        ViewModel.addPayloadAltitudeValue(payloadData.PayloadAltitude);
                        ViewModel.addPayloadSpeedValue(payloadData.PayloadSpeed);
                        ViewModel.addPayloadTempValue(payloadData.PayloadTemperature);
                        ViewModel.addPayloadPressureValue(payloadData.PayloadPressure);
                        ViewModel.addPayloadHumidityValue(payloadData.PayloadHumidity);
                    }

                    string statusText = "Serial verisi: " + DateTime.Now.ToString("HH:mm:ss");
                    if (rocketData != null) statusText += $" - Roket: #{rocketData.PacketCounter}";
                    if (payloadData != null) statusText += $" - Payload: #{payloadData.PacketCounter}";
                    
                    ViewModel.UpdateStatus(statusText);

                    _logger?.LogDebug("Chart'lar güncellendi - Roket: {HasRocket}, Payload: {HasPayload}", 
                        rocketData != null, payloadData != null);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Combined chart güncelleme hatasý");
                }
            });
        }

        /// <summary>
        /// Roket telemetrilerinden HYIDenem verisi oluþtur ve chart'larý güncelle
        /// </summary>
        public void GenerateHYIDenemeFromRocketAndUpdateCharts(RocketTelemetryData rocketData)
        {
            try
            {
                // Payload verileri için küçük random deðiþimler (±0.05)
                float payloadAltitudeVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadSpeedVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadTempVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadPressureVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);
                float payloadHumidityVariation = (float)(_random.NextDouble() * 2 * PAYLOAD_VARIATION - PAYLOAD_VARIATION);

                var hyiDenemeData = new HYIDenemeData
                {
                    // Temel bilgiler
                    TeamId = rocketData.TeamID,
                    PacketCounter = rocketData.PacketCounter,
                    
                    // TÜM ROKET TELEMETRÝLERÝ - AYNEN KOPYALA
                    RocketAltitude = rocketData.RocketAltitude,
                    RocketGpsAltitude = rocketData.RocketGpsAltitude,
                    RocketLatitude = rocketData.RocketLatitude,
                    RocketLongitude = rocketData.RocketLongitude,
                    GyroX = rocketData.GyroX,
                    GyroY = rocketData.GyroY,
                    GyroZ = rocketData.GyroZ,
                    AccelX = rocketData.AccelX,
                    AccelY = rocketData.AccelY,
                    AccelZ = rocketData.AccelZ,
                    Angle = rocketData.Angle,
                    RocketSpeed = rocketData.RocketSpeed,
                    RocketTemperature = rocketData.RocketTemperature,
                    RocketPressure = rocketData.RocketPressure,
                    RocketStatus = rocketData.status,
                    
                    // PAYLOAD VERÝLERÝ - GPS roketle ayný, diðerleri: sabit ±0.05
                    PayloadGpsAltitude = rocketData.RocketGpsAltitude, // Roket GPS irtifa ile ayný
                    PayloadLatitude = rocketData.RocketLatitude,       // Roket enlem ile ayný
                    PayloadLongitude = rocketData.RocketLongitude,     // Roket boylam ile ayný
                     
                    // Sabit deðerler ±0.05 deðiþim ile
                    PayloadAltitude = BASE_PAYLOAD_ALTITUDE + payloadAltitudeVariation,
                    PayloadSpeed = BASE_PAYLOAD_SPEED + payloadSpeedVariation,
                    PayloadTemperature = BASE_PAYLOAD_TEMPERATURE + payloadTempVariation,
                    PayloadPressure = BASE_PAYLOAD_PRESSURE + payloadPressureVariation,
                    PayloadHumidity = BASE_PAYLOAD_HUMIDITY + payloadHumidityVariation,
                    
                    CRC = 0 // Otomatik hesaplanacak
                };

                // Chart'larý güncelle
                UpdateChartsFromHYIDenem(hyiDenemeData);
                
                _logger?.LogInformation("HYIDenem verisi oluþturuldu ve chart'lar güncellendi: TeamID={TeamId}, Counter={Counter}", 
                    hyiDenemeData.TeamId, hyiDenemeData.PacketCounter);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "HYIDenem verisi oluþturma hatasý");
            }
        }
        #endregion

        #region Private Helper Methods
        /// <summary>
        /// ViewModel'i günceller
        /// </summary>
        private void UpdateViewModelData(float rocketAltitude, float payloadAltitude,
            float rocketSpeed, float payloadSpeed, float rocketTemp, float payloadTemp, 
            float rocketPressure, float payloadPressure, float payloadHumidity, 
            float accelX, float accelY, float accelZ)
        {
            if (ViewModel == null)
            {
                _logger?.LogWarning("UpdateViewModelData: ViewModel is null!");
                return;
            }

            ViewModel.AddRocketAltitudeValue(rocketAltitude);
            ViewModel.addPayloadAltitudeValue(payloadAltitude);
            ViewModel.addRocketAccelXValue(accelX);
            ViewModel.addRocketAccelYValue(accelY);
            ViewModel.addRocketAccelZValue(accelZ);
            ViewModel.addRocketSpeedValue(rocketSpeed);
            ViewModel.addPayloadSpeedValue(payloadSpeed);
            ViewModel.addRocketTempValue(rocketTemp);
            ViewModel.addPayloadTempValue(payloadTemp);
            ViewModel.addRocketPressureValue(rocketPressure);
            ViewModel.addPayloadPressureValue(payloadPressure);
            ViewModel.addPayloadHumidityValue(payloadHumidity);

            _logger?.LogDebug("ViewModel güncellendi - Roket Alt: {RocketAlt:F2}, Payload Alt: {PayloadAlt:F2}", 
                rocketAltitude, payloadAltitude);
        }
        #endregion
    }
}