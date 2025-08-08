using copilot_deneme.ViewModels;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI;

namespace copilot_deneme
{
    /// <summary>
    /// Arduino BNO055, BMP388 ve GPS sensör verilerini görüntüleyen sayfa
    /// </summary>
    public sealed partial class ArduinoPage : Page
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private SerialPort? _arduinoSerialPort;
        private bool _isConnected = false;
        private int _dataCounter = 0;

        // GPS Map deðiþkenleri
        private bool _isMapInitialized = false;
        private double _currentLatitude = 39.925533;  // Ankara baþlangýç konumu
        private double _currentLongitude = 32.866287;
        private double _currentGpsAltitude = 0;

        // Chart data collections
        private readonly ObservableCollection<ObservablePoint> _yawData;
        private readonly ObservableCollection<ObservablePoint> _pitchData;
        private readonly ObservableCollection<ObservablePoint> _rollData;
        private readonly ObservableCollection<ObservablePoint> _accelData;
        private readonly ObservableCollection<ObservablePoint> _pressureData;
        private readonly ObservableCollection<ObservablePoint> _altitudeData;
        private readonly ObservableCollection<ObservablePoint> _temperatureData;
        private readonly ObservableCollection<ObservablePoint> _pressureRateData;

        // Chart series
        private ISeries[] _yawSeries;
        private ISeries[] _pitchSeries;
        private ISeries[] _rollSeries;
        private ISeries[] _accelSeries;
        private ISeries[] _pressureSeries;
        private ISeries[] _altitudeSeries;
        private ISeries[] _temperatureSeries;
        private ISeries[] _pressureRateSeries;

        // Current values
        private float _currentYaw = 0;
        private float _currentPitch = 0;
        private float _currentRoll = 0;
        private float _currentAccel = 0;
        private float _currentPressure = 0;
        private float _currentAltitude = 0;
        private float _currentTemperature = 0;
        private float _currentPressureRate = 0;

        public ArduinoPage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // Initialize data collections
            _yawData = new ObservableCollection<ObservablePoint>();
            _pitchData = new ObservableCollection<ObservablePoint>();
            _rollData = new ObservableCollection<ObservablePoint>();
            _accelData = new ObservableCollection<ObservablePoint>();
            _pressureData = new ObservableCollection<ObservablePoint>();
            _altitudeData = new ObservableCollection<ObservablePoint>();
            _temperatureData = new ObservableCollection<ObservablePoint>();
            _pressureRateData = new ObservableCollection<ObservablePoint>();

            InitializeCharts();
            InitializeGpsMap();
            
            // SettingPage'den Arduino baðlantýsýný kontrol et
            CheckArduinoConnectionFromSettings();

            // SettingPage callback'ini kaydet
            SettingPage.ArduinoDataUpdateCallback = OnArduinoDataFromSettings;

            System.Diagnostics.Debug.WriteLine("ArduinoPage baþlatýldý - GPS harita sistemi ve SettingPage callback kaydedildi");
        }

        private void InitializeCharts()
        {
            // Yaw Chart
            _yawSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _yawData,
                    Stroke = new SolidColorPaint(SKColors.Gold) { StrokeThickness = 3 },
                    Fill = null,
                    GeometryStroke = null,
                    GeometryFill = null,
                    Name = "Yaw (°)"
                }
            };
            YawChart.Series = _yawSeries;

            // Pitch Chart
            _pitchSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _pitchData,
                    Stroke = new SolidColorPaint(SKColors.LightGreen) { StrokeThickness = 3 },
                    Fill = null,
                    GeometryStroke = null,
                    GeometryFill = null,
                    Name = "Pitch (°)"
                }
            };
            PitchChart.Series = _pitchSeries;

            // Roll Chart
            _rollSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _rollData,
                    Stroke = new SolidColorPaint(SKColors.LightCoral) { StrokeThickness = 3 },
                    Fill = null,
                    GeometryStroke = null,
                    GeometryFill = null,
                    Name = "Roll (°)"
                }
            };
            RollChart.Series = _rollSeries;

            // Acceleration Chart
            _accelSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _accelData,
                    Stroke = new SolidColorPaint(SKColors.Orange) { StrokeThickness = 3 },
                    Fill = null,
                    GeometryStroke = null,
                    GeometryFill = null,
                    Name = "Toplam Ývme (m/s²)"
                }
            };
            AccelChart.Series = _accelSeries;

            // Pressure Chart
            _pressureSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _pressureData,
                    Stroke = new SolidColorPaint(SKColors.MediumOrchid) { StrokeThickness = 3 },
                    Fill = null,
                    GeometryStroke = null,
                    GeometryFill = null,
                    Name = "Basýnç (hPa)"
                }
            };
            PressureChart.Series = _pressureSeries;

            // Altitude Chart
            _altitudeSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _altitudeData,
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 3 },
                    Fill = null,
                    GeometryStroke = null,
                    GeometryFill = null,
                    Name = "Ýrtifa (m)"
                }
            };
            AltitudeChart.Series = _altitudeSeries;

            // Temperature Chart
            _temperatureSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _temperatureData,
                    Stroke = new SolidColorPaint(SKColors.Crimson) { StrokeThickness = 3 },
                    Fill = null,
                    GeometryStroke = null,
                    GeometryFill = null,
                    Name = "Sýcaklýk (°C)"
                }
            };
            TemperatureChart.Series = _temperatureSeries;

            // Pressure Rate Chart
            _pressureRateSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _pressureRateData,
                    Stroke = new SolidColorPaint(SKColors.Orange) { StrokeThickness = 3 },
                    Fill = null,
                    GeometryStroke = null,
                    GeometryFill = null,
                    Name = "dP/dt (hPa/s)"
                }
            };
            PressureRateChart.Series = _pressureRateSeries;

            System.Diagnostics.Debug.WriteLine("Arduino chart'larý baþlatýldý");
        }

        private async void InitializeGpsMap()
        {
            try
            {
                await ArduinoMapWebView.EnsureCoreWebView2Async();
                string mapHtml = CreateArduinoMapHtml();
                ArduinoMapWebView.NavigateToString(mapHtml);
                _isMapInitialized = true;
                System.Diagnostics.Debug.WriteLine("Arduino GPS harita baþarýyla baþlatýldý");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino GPS harita baþlatma hatasý: {ex.Message}");
            }
        }

        private string CreateArduinoMapHtml()
        {
            string mapHtml = @"<!DOCTYPE html>
<html>
<head>
    <title>Arduino GPS Takip Sistemi</title>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css' />
    <script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
    <style>
        body { margin: 0; padding: 0; font-family: Arial, sans-serif; }
        #map { height: 100vh; width: 100%; }
        .arduino-marker {
            background: #FFD93D;
            width: 20px;
            height: 20px;
            border-radius: 50%;
            border: 4px solid white;
            box-shadow: 0 0 10px rgba(255,217,61,0.9);
        }
        .info-panel {
            position: absolute;
            top: 15px;
            right: 15px;
            background: rgba(0,0,0,0.85);
            color: white;
            padding: 12px 16px;
            border-radius: 8px;
            font-size: 13px;
            z-index: 1000;
            min-width: 200px;
        }
        .altitude-panel {
            position: absolute;
            top: 15px;
            left: 15px;
            background: rgba(0,0,0,0.85);
            color: white;
            padding: 12px 16px;
            border-radius: 8px;
            font-size: 12px;
            z-index: 1000;
            min-width: 160px;
        }
    </style>
</head>
<body>
    <div id='map'></div>
    <div class='info-panel'>
        <div><strong>Arduino GPS Pozisyonu</strong></div>
        <div>Lat: <span id='current-lat'>---.------</span></div>
        <div>Lon: <span id='current-lon'>---.------</span></div>
        <div>Son Güncelleme: <span id='last-update'>--:--:--</span></div>
    </div>
    <div class='altitude-panel'>
        <div><strong>Ýrtifa Bilgisi</strong></div>
        <div>GPS: <span id='gps-altitude'>0.0 m</span></div>
        <div>Baro: <span id='baro-altitude'>0.0 m</span></div>
    </div>
    <script>
        var map = L.map('map').setView([39.925533, 32.866287], 15);
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png').addTo(map);
        
        var arduinoIcon = L.divIcon({
            html: '<div class=""arduino-marker""></div>',
            iconSize: [28, 28],
            className: ''
        });
        
        var arduinoMarker = L.marker([39.925533, 32.866287], {icon: arduinoIcon}).addTo(map);
        var movementPath = L.polyline([], {color: '#FFD93D', weight: 4, opacity: 0.8}).addTo(map);
        
        var lastUpdatePosition = null;
        var mapInitialized = false;
        
        window.updateArduinoPosition = function(lat, lon, gpsAlt, baroAlt) {
            // Sadece gerçek GPS koordinatlarý için güncelle (0,0 deðil)
            if (lat !== 0 && lon !== 0) {
                var newPos = [lat, lon];
                
                // Ýlk kez geçerli koordinat geldiðinde haritayý oraya taþý
                if (!mapInitialized) {
                    map.setView(newPos, 16);
                    mapInitialized = true;
                } else {
                    // Sonraki güncellemelerde sadece marker'ý taþý, haritayý ortalama
                    var currentCenter = map.getCenter();
                    var distance = map.distance(currentCenter, newPos);
                    
                    // Sadece 100m'den fazla hareket varsa haritayý kaydýr
                    if (distance > 100) {
                        map.panTo(newPos);
                    }
                }
                
                arduinoMarker.setLatLng(newPos);
                movementPath.addLatLng(newPos);
                
                document.getElementById('current-lat').textContent = lat.toFixed(6);
                document.getElementById('current-lon').textContent = lon.toFixed(6);
                document.getElementById('gps-altitude').textContent = gpsAlt.toFixed(1) + ' m';
                document.getElementById('baro-altitude').textContent = baroAlt.toFixed(1) + ' m';
                
                var now = new Date();
                var timeStr = now.getHours().toString().padStart(2, '0') + ':' + 
                             now.getMinutes().toString().padStart(2, '0') + ':' + 
                             now.getSeconds().toString().padStart(2, '0');
                document.getElementById('last-update').textContent = timeStr;
                
                lastUpdatePosition = newPos;
            }
        };
    </script>
</body>
</html>";
            return mapHtml;
        }

        private async void UpdateArduinoGpsPosition(double latitude, double longitude, double gpsAltitude, double barometricAltitude)
        {
            if (!_isMapInitialized) return;
            
            try
            {
                // Sadece geçerli koordinatlarý güncelle (0,0 olmayan gerçek koordinatlar)
                if (latitude != 0 && longitude != 0)
                {
                    _currentLatitude = latitude;
                    _currentLongitude = longitude;
                    _currentGpsAltitude = gpsAltitude;
                }
                
                // JavaScript fonksiyonunu çaðýr
                string script = $"updateArduinoPosition({_currentLatitude.ToString(CultureInfo.InvariantCulture)}, " +
                               $"{_currentLongitude.ToString(CultureInfo.InvariantCulture)}, " +
                               $"{_currentGpsAltitude.ToString(CultureInfo.InvariantCulture)}, " +
                               $"{barometricAltitude.ToString(CultureInfo.InvariantCulture)})";
                
                await ArduinoMapWebView.ExecuteScriptAsync(script);
                
                // UI güncellemeleri
                CurrentLatitudeText.Text = $"{_currentLatitude:F6}";
                CurrentLongitudeText.Text = $"{_currentLongitude:F6}";
                
                // GPS durumunu güncelle
                if (latitude != 0 && longitude != 0)
                {
                    GpsStatusText.Text = "FIX";
                    GpsStatusText.Foreground = new SolidColorBrush(Colors.LightGreen);
                }
                else
                {
                    GpsStatusText.Text = "NO FIX";
                    GpsStatusText.Foreground = new SolidColorBrush(Colors.Orange);
                }
                
                System.Diagnostics.Debug.WriteLine($"Arduino GPS pozisyonu güncellendi: {_currentLatitude:F6}, {_currentLongitude:F6} ({_currentGpsAltitude:F1}m)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino GPS pozisyon güncelleme hatasý: {ex.Message}");
            }
        }

        // SettingPage'den gelen Arduino verilerini iþle
        private void OnArduinoDataFromSettings(float yaw, float pitch, float roll, float accel, float pressure, float altitude, float temperature, float pressureRate)
        {
            try
            {
                // Veri güncelle
                _currentYaw = yaw;
                _currentPitch = pitch;
                _currentRoll = roll;
                _currentAccel = accel;
                _currentPressure = pressure;
                _currentAltitude = altitude;
                _currentTemperature = temperature;
                _currentPressureRate = pressureRate;

                // UI thread'de chart'larý güncelle
                _dispatcherQueue.TryEnqueue(() =>
                {
                    UpdateChartsAndUI();
                    
                    // GPS koordinatlarý - gerçek GPS verisi gelene kadar mevcut konumu koru
                    // Sadece valid GPS verisi varsa güncelle
                    // NOT: Bu test kodu - gerçek GPS koordinatlarý Arduino'dan gelecek
                    
                    // GPS haritasýný sadece gerçek GPS verisi geldiðinde güncelle
                    // Þimdilik test amaçlý olarak mevcut konumu koruyalým
                    if (_currentLatitude != 39.925533 || _currentLongitude != 32.866287)
                    {
                        // Sadece daha önce valid GPS verisi almýþsak güncelle
                        UpdateArduinoGpsPosition(_currentLatitude, _currentLongitude, altitude + 10, altitude);
                    }
                });

                System.Diagnostics.Debug.WriteLine($"ArduinoPage: SettingPage'den veri alýndý - Ýrtifa: {altitude:F1}m, Sýcaklýk: {temperature:F1}°C");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ArduinoPage: SettingPage veri iþleme hatasý: {ex.Message}");
            }
        }

        private void ArduinoSerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_arduinoSerialPort?.IsOpen != true) return;

                string data = _arduinoSerialPort.ReadLine().Trim();
                
                if (!string.IsNullOrEmpty(data))
                {
                    ParseArduinoTelemetryData(data);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino veri okuma hatasý: {ex.Message}");
            }
        }

        private void ParseArduinoTelemetryData(string data)
        {
            try
            {
                // Debug için ham veriyi logla
                System.Diagnostics.Debug.WriteLine($"Arduino HAM VERÝ: {data}");
                
                // Arduino telemetri formatýný parse et:
                // "Rocket Telemetry: Alt: 55.75 m, GPS Alt: 29.90 m, Lat: 38.511806, Lon: 27.030424, Gyro: -0.06, 0.13, -0.06 | Accel: 0.10, -0.11, 9.32 | Angle: 0.06 deg, Temp: 35.73 °C, Press: 1006.57 hPa, Speed: 0.00 m/s"
                
                if (data.Contains("Rocket Telemetry:"))
                {
                    var altMatch = Regex.Match(data, @"Alt:\s*([\d\.-]+)\s*m");
                    var gpsAltMatch = Regex.Match(data, @"GPS Alt:\s*([\d\.-]+)\s*m");
                    var latMatch = Regex.Match(data, @"Lat:\s*([\d\.-]+)");
                    var lonMatch = Regex.Match(data, @"Lon:\s*([\d\.-]+)");
                    var gyroMatches = Regex.Matches(data, @"Gyro:\s*([\d\.-]+),\s*([\d\.-]+),\s*([\d\.-]+)");
                    var accelMatches = Regex.Matches(data, @"Accel:\s*([\d\.-]+),\s*([\d\.-]+),\s*([\d\.-]+)");
                    var tempMatch = Regex.Match(data, @"Temp:\s*([\d\.-]+)\s*°C");
                    var pressMatch = Regex.Match(data, @"Press:\s*([\d\.-]+)\s*hPa");

                    bool hasValidData = false;
                    double latitude = 0, longitude = 0, gpsAltitude = 0;

                    // GPS koordinatlarý parse et
                    if (latMatch.Success && double.TryParse(latMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude))
                    {
                        hasValidData = true;
                    }

                    if (lonMatch.Success && double.TryParse(lonMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
                    {
                        hasValidData = true;
                    }

                    if (gpsAltMatch.Success && double.TryParse(gpsAltMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out gpsAltitude))
                    {
                        hasValidData = true;
                    }

                    // Barometrik irtifa
                    if (altMatch.Success && float.TryParse(altMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float altitude))
                    {
                        if (altitude >= -1000 && altitude <= 50000)
                        {
                            _currentAltitude = altitude;
                            hasValidData = true;
                        }
                    }

                    // Gyro verileri (Yaw, Pitch, Roll)
                    if (gyroMatches.Count > 0 && gyroMatches[0].Groups.Count >= 4)
                    {
                        if (float.TryParse(gyroMatches[0].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float gyroX))
                            _currentYaw = gyroX;
                        if (float.TryParse(gyroMatches[0].Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float gyroY))
                            _currentPitch = gyroY;
                        if (float.TryParse(gyroMatches[0].Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float gyroZ))
                            _currentRoll = gyroZ;
                        hasValidData = true;
                    }

                    // Accelerometer verileri
                    if (accelMatches.Count > 0 && accelMatches[0].Groups.Count >= 4)
                    {
                        float accelX = 0, accelY = 0, accelZ = 0;
                        if (float.TryParse(accelMatches[0].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out accelX) &&
                            float.TryParse(accelMatches[0].Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out accelY) &&
                            float.TryParse(accelMatches[0].Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out accelZ))
                        {
                            // Toplam ivme hesapla
                            _currentAccel = (float)Math.Sqrt(accelX * accelX + accelY * accelY + accelZ * accelZ);
                            hasValidData = true;
                        }
                    }

                    // Sýcaklýk
                    if (tempMatch.Success && float.TryParse(tempMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float temperature))
                    {
                        if (temperature >= -50 && temperature <= 85)
                        {
                            _currentTemperature = temperature;
                            hasValidData = true;
                        }
                    }

                    // Basýnç
                    if (pressMatch.Success && float.TryParse(pressMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pressure))
                    {
                        _currentPressure = pressure;
                        hasValidData = true;
                    }

                    if (hasValidData)
                    {
                        _dispatcherQueue.TryEnqueue(() =>
                        {
                            UpdateChartsAndUI();
                            
                            // GPS haritasýný güncelle
                            if (latitude != 0 && longitude != 0)
                            {
                                UpdateArduinoGpsPosition(latitude, longitude, gpsAltitude, _currentAltitude);
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino veri parse hatasý: {ex.Message}");
            }
        }

        private void UpdateChartsAndUI()
        {
            try
            {
                _dataCounter++;
                double timeStamp = _dataCounter;

                // Chart verilerini güncelle (maksimum 100 nokta)
                const int maxPoints = 100;

                // Yaw data
                _yawData.Add(new ObservablePoint(timeStamp, _currentYaw));
                if (_yawData.Count > maxPoints) _yawData.RemoveAt(0);

                // Pitch data
                _pitchData.Add(new ObservablePoint(timeStamp, _currentPitch));
                if (_pitchData.Count > maxPoints) _pitchData.RemoveAt(0);

                // Roll data
                _rollData.Add(new ObservablePoint(timeStamp, _currentRoll));
                if (_rollData.Count > maxPoints) _rollData.RemoveAt(0);

                // Acceleration data
                _accelData.Add(new ObservablePoint(timeStamp, _currentAccel));
                if (_accelData.Count > maxPoints) _accelData.RemoveAt(0);

                // Pressure data
                _pressureData.Add(new ObservablePoint(timeStamp, _currentPressure));
                if (_pressureData.Count > maxPoints) _pressureData.RemoveAt(0);

                // Altitude data
                _altitudeData.Add(new ObservablePoint(timeStamp, _currentAltitude));
                if (_altitudeData.Count > maxPoints) _altitudeData.RemoveAt(0);

                // Temperature data
                _temperatureData.Add(new ObservablePoint(timeStamp, _currentTemperature));
                if (_temperatureData.Count > maxPoints) _temperatureData.RemoveAt(0);

                // Pressure rate data
                _pressureRateData.Add(new ObservablePoint(timeStamp, _currentPressureRate));
                if (_pressureRateData.Count > maxPoints) _pressureRateData.RemoveAt(0);

                System.Diagnostics.Debug.WriteLine($"Zaman damgasý: {timeStamp}, Yaw: {_currentYaw}, Pitch: {_currentPitch}, Roll: {_currentRoll}, Ývme: {_currentAccel}, Basýnç: {_currentPressure}, Ýrtifa: {_currentAltitude}, Sýcaklýk: {_currentTemperature}, dP/dt: {_currentPressureRate}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Grafik ve UI güncelleme hatasý: {ex.Message}");
            }
        }

        private void CheckArduinoConnectionFromSettings()
        {
            try
            {
                // SettingPage'den Arduino SerialPort instance'ýný al
                var settingsArduinoPort = SettingPage.GetArduinoSerialPortService();
                
                if (settingsArduinoPort?.IsOpen == true)
                {
                    // Mevcut event handler'ý kaldýr
                    if (_arduinoSerialPort != null)
                    {
                        _arduinoSerialPort.DataReceived -= ArduinoSerialPort_DataReceived;
                    }
                    
                    // SettingPage'den gelen baðlantýyý kullan
                    _arduinoSerialPort = settingsArduinoPort;
                    _arduinoSerialPort.DataReceived += ArduinoSerialPort_DataReceived;
                    _isConnected = true;
                    
                    ConnectionIndicator.Fill = new SolidColorBrush(Colors.LightGreen);
                    ConnectionStatusText.Text = $"SettingPage'den baðlý: {_arduinoSerialPort.PortName}";
                    
                    System.Diagnostics.Debug.WriteLine($"ArduinoPage SettingPage baðlantýsýný kullanýyor: {_arduinoSerialPort.PortName}");
                }
                else
                {
                    // SettingPage'de Arduino baðlý deðil, otomatik baðlanmayý dene
                    TryConnectToArduino();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage Arduino baðlantý kontrolü hatasý: {ex.Message}");
                // Hata durumunda otomatik baðlanmayý dene
                TryConnectToArduino();
            }
        }

        private async void TryConnectToArduino()
        {
            try
            {
                // Önce SettingPage'de baðlý bir Arduino var mý kontrol et
                var settingsArduinoPort = SettingPage.GetArduinoSerialPortService();
                if (settingsArduinoPort?.IsOpen == true)
                {
                    ConnectionStatusText.Text = "SettingPage'de Arduino zaten baðlý. Manuel baðlantý kullanýlýyor.";
                    return;
                }
                
                // Mevcut Arduino portlarýný bul
                string[] availablePorts = SerialPort.GetPortNames();
                
                if (availablePorts.Length == 0)
                {
                    ConnectionStatusText.Text = "Hiç seri port bulunamadý";
                    return;
                }

                ConnectionStatusText.Text = "Arduino otomatik taranýyor...";

                // Arduino'yu bulmaya çalýþ (115200 baud rate ile)
                foreach (string portName in availablePorts)
                {
                    try
                    {
                        ConnectionStatusText.Text = $"Port deneniyor: {portName}";
                        
                        if (await TryConnectToPort(portName, 115200))
                        {
                            _isConnected = true;
                            ConnectionIndicator.Fill = new SolidColorBrush(Colors.LightGreen);
                            ConnectionStatusText.Text = $"Arduino otomatik baðlandý: {portName}";
                            System.Diagnostics.Debug.WriteLine($"Arduino baþarýyla baðlandý: {portName}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Port {portName} baðlantý hatasý: {ex.Message}");
                        continue;
                    }
                }

                // Hiçbir port çalýþmadý
                ConnectionStatusText.Text = "Arduino bulunamadý. SettingPage'den manuel baðlantý deneyebilirsiniz.";
            }
            catch (Exception ex)
            {
                ConnectionStatusText.Text = $"Baðlantý hatasý: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Arduino baðlantý genel hatasý: {ex.Message}");
            }
        }

        private async Task<bool> TryConnectToPort(string portName, int baudRate)
        {
            try
            {
                // Mevcut baðlantýyý kapat
                DisconnectArduino();

                _arduinoSerialPort = new SerialPort(portName, baudRate)
                {
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = 2000,
                    WriteTimeout = 2000
                };

                _arduinoSerialPort.DataReceived += ArduinoSerialPort_DataReceived;
                _arduinoSerialPort.Open();

                // Port açýldýktan sonra 1 saniye bekle
                await Task.Delay(1000);

                return _arduinoSerialPort.IsOpen;
            }
            catch
            {
                return false;
            }
        }

        private void DisconnectArduino()
        {
            try
            {
                // Event handler'ý kaldýr
                if (_arduinoSerialPort != null)
                {
                    _arduinoSerialPort.DataReceived -= ArduinoSerialPort_DataReceived;
                }
                
                // Eðer bu ArduinoPage'in kendi baðlantýsýysa kapat
                // SettingPage'den geliyorsa kapatma
                var settingsArduinoPort = SettingPage.GetArduinoSerialPortService();
                if (_arduinoSerialPort != settingsArduinoPort && _arduinoSerialPort?.IsOpen == true)
                {
                    _arduinoSerialPort.Close();
                    _arduinoSerialPort?.Dispose();
                }
                
                _arduinoSerialPort = null;
                _isConnected = false;
                
                System.Diagnostics.Debug.WriteLine("ArduinoPage Arduino baðlantýsý kesildi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino kapatma hatasý: {ex.Message}");
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SettingPage'den Arduino baðlantýsýný kontrol et
            CheckArduinoConnectionFromSettings();
            
            System.Diagnostics.Debug.WriteLine("ArduinoPage navigasyonu tamamlandý - GPS harita sistemi hazýr");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // Event handler'ý kaldýr eðer varsa
            if (_arduinoSerialPort != null)
            {
                _arduinoSerialPort.DataReceived -= ArduinoSerialPort_DataReceived;
            }
            
            // SettingPage callback'ini temizle
            SettingPage.ArduinoDataUpdateCallback = null;
            
            // Kendi baðlantýsýný kapatma - SettingPage'in yönettiði baðlantýyý bozmayalým
            _arduinoSerialPort = null;
            _isConnected = false;
            
            System.Diagnostics.Debug.WriteLine("ArduinoPage'den ayrýldý - Event handler kaldýrýldý, callback temizlendi");
        }

        // Sayfa dispose edildiðinde
        public void Dispose()
        {
            try
            {
                // Kendi SerialPort'unu dispose etme - SettingPage'in yönetiyor
                // Sadece event handler'ý kaldýr
                if (_arduinoSerialPort != null)
                {
                    _arduinoSerialPort.DataReceived -= ArduinoSerialPort_DataReceived;
                }
                
                // SettingPage callback'ini temizle
                SettingPage.ArduinoDataUpdateCallback = null;
                
                System.Diagnostics.Debug.WriteLine("ArduinoPage dispose edildi - callback temizlendi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ArduinoPage dispose hatasý: {ex.Message}");
            }
        }
    }
}