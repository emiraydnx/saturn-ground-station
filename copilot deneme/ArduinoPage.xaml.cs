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
    /// Arduino BNO055 ve BMP388 sens�r verilerini g�r�nt�leyen sayfa
    /// </summary>
    public sealed partial class ArduinoPage : Page
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private SerialPort? _arduinoSerialPort;
        private bool _isConnected = false;
        private int _dataCounter = 0;

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
            
            // SettingPage'den Arduino ba�lant�s�n� kontrol et
            CheckArduinoConnectionFromSettings();

            // SettingPage callback'ini kaydet
            SettingPage.ArduinoDataUpdateCallback = OnArduinoDataFromSettings;

            System.Diagnostics.Debug.WriteLine("ArduinoPage ba�lat�ld� - SettingPage callback kaydedildi");
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
                    Name = "Yaw (�)"
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
                    Name = "Pitch (�)"
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
                    Name = "Roll (�)"
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
                    Name = "Toplam �vme (m/s�)"
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
                    Name = "Bas�n� (hPa)"
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
                    Name = "�rtifa (m)"
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
                    Name = "S�cakl�k (�C)"
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

            System.Diagnostics.Debug.WriteLine("Arduino chart'lar� ba�lat�ld�");
        }

        private async void TryConnectToArduino()
        {
            try
            {
                // �nce SettingPage'de ba�l� bir Arduino var m� kontrol et
                var settingsArduinoPort = SettingPage.GetArduinoSerialPortService();
                if (settingsArduinoPort?.IsOpen == true)
                {
                    ConnectionStatusText.Text = "SettingPage'de Arduino zaten ba�l�. Manuel ba�lant� kullan�l�yor.";
                    return;
                }
                
                // Mevcut Arduino portlar�n� bul
                string[] availablePorts = SerialPort.GetPortNames();
                
                if (availablePorts.Length == 0)
                {
                    ConnectionStatusText.Text = "Hi� seri port bulunamad�";
                    return;
                }

                ConnectionStatusText.Text = "Arduino otomatik taran�yor...";

                // Arduino'yu bulmaya �al�� (115200 baud rate ile)
                foreach (string portName in availablePorts)
                {
                    try
                    {
                        ConnectionStatusText.Text = $"Port deneniyor: {portName}";
                        
                        if (await TryConnectToPort(portName, 115200))
                        {
                            _isConnected = true;
                            ConnectionIndicator.Fill = new SolidColorBrush(Colors.LightGreen);
                            ConnectionStatusText.Text = $"Arduino otomatik ba�land�: {portName}";
                            System.Diagnostics.Debug.WriteLine($"Arduino ba�ar�yla ba�land�: {portName}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Port {portName} ba�lant� hatas�: {ex.Message}");
                        continue;
                    }
                }

                // Hi�bir port �al��mad�
                ConnectionStatusText.Text = "Arduino bulunamad�. SettingPage'den manuel ba�lant� deneyebilirsiniz.";
            }
            catch (Exception ex)
            {
                ConnectionStatusText.Text = $"Ba�lant� hatas�: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Arduino ba�lant� genel hatas�: {ex.Message}");
            }
        }

        private async Task<bool> TryConnectToPort(string portName, int baudRate)
        {
            try
            {
                // Mevcut ba�lant�y� kapat
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

                // Port a��ld�ktan sonra 1 saniye bekle
                await Task.Delay(1000);

                6return _arduinoSerialPort.IsOpen;
            }
            catch
            {
                return false;
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
                    ParseArduinoData(data);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino veri okuma hatas�: {ex.Message}");
            }
        }

        private void ParseArduinoData(string data)
        {
            try
            {
                // Debug i�in ham veriyi logla
                System.Diagnostics.Debug.WriteLine($"Arduino HAM VER�: {data}");
                
                // Arduino ��kt� format�:
                // "Yaw: 123.4 | Pitch: 12.3 | Roll: 45.6 | Accel: 9.81 m/s^2 | Pressure: 1013.25 hPa | Altitude: 123.45 m | Temp: 25.67 �C | dP/dt: 0.123 hPa/s"
                
                var yawMatch = Regex.Match(data, @"Yaw:\s*([\d\.-]+)");
                var pitchMatch = Regex.Match(data, @"Pitch:\s*([\d\.-]+)");
                var rollMatch = Regex.Match(data, @"Roll:\s*([\d\.-]+)");
                var accelMatch = Regex.Match(data, @"Accel:\s*([\d\.-]+)");
                var pressureMatch = Regex.Match(data, @"Pressure:\s*([\d\.-]+)");
                var altitudeMatch = Regex.Match(data, @"Altitude:\s*([\d\.-]+)");
                var temperatureMatch = Regex.Match(data, @"Temperature:\s*([\d\.-]+)");
                var pressureRateMatch = Regex.Match(data, @"PressureRate:\s*([\d\.-]+)");

                bool hasValidData = false;

                if (yawMatch.Success && float.TryParse(yawMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw))
                {
                    _currentYaw = yaw;
                    hasValidData = true;
                }

                if (pitchMatch.Success && float.TryParse(pitchMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pitch))
                {
                    _currentPitch = pitch;
                    hasValidData = true;
                }

                if (rollMatch.Success && float.TryParse(rollMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float roll))
                {
                    _currentRoll = roll;
                    hasValidData = true;
                }

                if (accelMatch.Success && float.TryParse(accelMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float accel))
                {
                    _currentAccel = accel;
                    hasValidData = true;
                }

                if (pressureMatch.Success && float.TryParse(pressureMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pressure))
                {
                    _currentPressure = pressure;
                    hasValidData = true;
                }

                if (altitudeMatch.Success && float.TryParse(altitudeMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float altitude))
                {
                    // �rtifa de�erini makul s�n�rlar i�inde kontrol et
                    if (altitude >= -1000 && altitude <= 50000) // -1000m ile 50000m aras� makul de�erler
                    {
                        _currentAltitude = altitude;
                        hasValidData = true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Arduino �rtifa D�KKAT: A��r� de�er filtrelendi: {altitude}m");
                    }
                }

                if (temperatureMatch.Success && float.TryParse(temperatureMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float temperature))
                {
                    // S�cakl�k de�erini makul s�n�rlar i�inde kontrol et
                    if (temperature >= -50 && temperature <= 85) // -50�C ile 85�C aras� makul de�erler
                    {
                        _currentTemperature = temperature;
                        hasValidData = true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Arduino S�cakl�k D�KKAT: A��r� de�er filtrelendi: {temperature}�C");
                    }
                }

                if (pressureRateMatch.Success && float.TryParse(pressureRateMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float pressureRate))
                {
                    _currentPressureRate = pressureRate;
                    hasValidData = true;
                }

                if (hasValidData)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateChartsAndUI();
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino veri parse hatas�: {ex.Message}");
            }
        }

        private void UpdateChartsAndUI()
        {
            try
            {
                _dataCounter++;
                double timeStamp = _dataCounter;

                // Chart verilerini g�ncelle (maksimum 100 nokta)
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

                // UI g�ncellemeleri
                CurrentYawText.Text = $"{_currentYaw:F1}�";
                CurrentPitchText.Text = $"{_currentPitch:F1}�";
                CurrentRollText.Text = $"{_currentRoll:F1}�";
                CurrentAccelText.Text = $"{_currentAccel:F2} m/s�";
                CurrentPressureText.Text = $"{_currentPressure:F2} hPa";
                CurrentAltitudeText.Text = $"{_currentAltitude:F1} m";
                CurrentTemperatureText.Text = $"{_currentTemperature:F1}�C";
                CurrentPressureRateText.Text = $"{_currentPressureRate:F3} hPa/s";

                DataCountText.Text = _dataCounter.ToString();
                LastUpdateText.Text = $"Son G�ncelleme: {DateTime.Now:HH:mm:ss}";

                System.Diagnostics.Debug.WriteLine($"Arduino verisi g�ncellendi #{_dataCounter} - Yaw: {_currentYaw:F1}�, Pitch: {_currentPitch:F1}�, �rtifa: {_currentAltitude:F1}m, S�cakl�k: {_currentTemperature:F1}�C, dP/dt: {_currentPressureRate:F3} hPa/s");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino chart g�ncelleme hatas�: {ex.Message}");
            }
        }

        private void DisconnectArduino()
        {
            try
            {
                // Event handler'� kald�r
                if (_arduinoSerialPort != null)
                {
                    _arduinoSerialPort.DataReceived -= ArduinoSerialPort_DataReceived;
                }
                
                // E�er bu ArduinoPage'in kendi ba�lant�s�ysa kapat
                // SettingPage'den geliyorsa kapatma
                var settingsArduinoPort = SettingPage.GetArduinoSerialPortService();
                if (_arduinoSerialPort != settingsArduinoPort && _arduinoSerialPort?.IsOpen == true)
                {
                    _arduinoSerialPort.Close();
                    _arduinoSerialPort?.Dispose();
                }
                
                _arduinoSerialPort = null;
                _isConnected = false;
                
                System.Diagnostics.Debug.WriteLine("ArduinoPage Arduino ba�lant�s� kesildi");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Arduino kapatma hatas�: {ex.Message}");
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SettingPage'den Arduino ba�lant�s�n� kontrol et
            CheckArduinoConnectionFromSettings();
            
            System.Diagnostics.Debug.WriteLine("ArduinoPage navigasyonu tamamland�");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // Event handler'� kald�r e�er varsa
            if (_arduinoSerialPort != null)
            {
                _arduinoSerialPort.DataReceived -= ArduinoSerialPort_DataReceived;
            }
            
            // SettingPage callback'ini temizle
            SettingPage.ArduinoDataUpdateCallback = null;
            
            // Kendi ba�lant�s�n� kapatma - SettingPage'in y�netti�i ba�lant�y� bozmayal�m
            _arduinoSerialPort = null;
            _isConnected = false;
            
            System.Diagnostics.Debug.WriteLine("ArduinoPage'den ayr�ld� - Event handler kald�r�ld�, callback temizlendi");
        }

        // Sayfa dispose edildi�inde
        public void Dispose()
        {
            try
            {
                // Kendi SerialPort'unu dispose etme - SettingPage'in y�netiyor
                // Sadece event handler'� kald�r
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
                System.Diagnostics.Debug.WriteLine($"ArduinoPage dispose hatas�: {ex.Message}");
            }
        }

        private void CheckArduinoConnectionFromSettings()
        {
            try
            {
                // SettingPage'den Arduino SerialPort instance'�n� al
                var settingsArduinoPort = SettingPage.GetArduinoSerialPortService();
                
                if (settingsArduinoPort?.IsOpen == true)
                {
                    // Mevcut event handler'� kald�r
                    if (_arduinoSerialPort != null)
                    {
                        _arduinoSerialPort.DataReceived -= ArduinoSerialPort_DataReceived;
                    }
                    
                    // SettingPage'den gelen ba�lant�y� kullan
                    _arduinoSerialPort = settingsArduinoPort;
                    _arduinoSerialPort.DataReceived += ArduinoSerialPort_DataReceived;
                    _isConnected = true;
                    
                    ConnectionIndicator.Fill = new SolidColorBrush(Colors.LightGreen);
                    ConnectionStatusText.Text = $"SettingPage'den ba�l�: {_arduinoSerialPort.PortName}";
                    
                    System.Diagnostics.Debug.WriteLine($"ArduinoPage SettingPage ba�lant�s�n� kullan�yor: {_arduinoSerialPort.PortName}");
                }
                else
                {
                    // SettingPage'de Arduino ba�l� de�il, otomatik ba�lanmay� dene
                    TryConnectToArduino();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage Arduino ba�lant� kontrol� hatas�: {ex.Message}");
                // Hata durumunda otomatik ba�lanmay� dene
                TryConnectToArduino();
            }
        }

        private void OnArduinoDataFromSettings(string data)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage'den al�nan veri: {data}");
                
                // Gelen veri format�:
                // "Yaw:123.45;Pitch:67.89;Roll:10.11;Accel:9.81;Pressure:1013.25;Altitude:123.45;Temp:25.67;dP/dt:0.123"
                
                var pairs = data.Split(';');
                var dataDict = new System.Collections.Generic.Dictionary<string, string>();

                foreach (var pair in pairs)
                {
                    var keyValue = pair.Split(':');
                    if (keyValue.Length == 2)
                    {
                        dataDict[keyValue[0].Trim()] = keyValue[1].Trim();
                    }
                }

                // Verileri ay�kla
                if (dataDict.TryGetValue("Yaw", out string? yawStr) && float.TryParse(yawStr, out float yaw))
                {
                    _currentYaw = yaw;
                }

                if (dataDict.TryGetValue("Pitch", out string? pitchStr) && float.TryParse(pitchStr, out float pitch))
                {
                    _currentPitch = pitch;
                }

                if (dataDict.TryGetValue("Roll", out string? rollStr) && float.TryParse(rollStr, out float roll))
                {
                    _currentRoll = roll;
                }

                if (dataDict.TryGetValue("Accel", out string? accelStr) && float.TryParse(accelStr, out float accel))
                {
                    _currentAccel = accel;
                }

                if (dataDict.TryGetValue("Pressure", out string? pressureStr) && float.TryParse(pressureStr, out float pressure))
                {
                    _currentPressure = pressure;
                }

                if (dataDict.TryGetValue("Altitude", out string? altitudeStr) && float.TryParse(altitudeStr, out float altitude))
                {
                    _currentAltitude = altitude;
                }

                if (dataDict.TryGetValue("Temp", out string? tempStr) && float.TryParse(tempStr, out float temp))
                {
                    _currentTemperature = temp;
                }

                if (dataDict.TryGetValue("dP/dt", out string? dpdtStr) && float.TryParse(dpdtStr, out float dpdt))
                {
                    _currentPressureRate = dpdt;
                }

                // UI ve grafikleri g�ncelle
                _dispatcherQueue.TryEnqueue(() =>
                {
                    UpdateChartsAndUI();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingPage veri i�leme hatas�: {ex.Message}");
            }
        }

        // SettingPage'den gelen Arduino verilerini i�le
        private void OnArduinoDataFromSettings(float yaw, float pitch, float roll, float accel, float pressure, float altitude, float temperature, float pressureRate)
        {
            try
            {
                // Veri g�ncelle
                _currentYaw = yaw;
                _currentPitch = pitch;
                _currentRoll = roll;
                _currentAccel = accel;
                _currentPressure = pressure;
                _currentAltitude = altitude;
                _currentTemperature = temperature;
                _currentPressureRate = pressureRate;

                // UI thread'de chart'lar� g�ncelle
                _dispatcherQueue.TryEnqueue(() =>
                {
                    UpdateChartsAndUI();
                });

                System.Diagnostics.Debug.WriteLine($"ArduinoPage: SettingPage'den veri al�nd� - �rtifa: {altitude:F1}m, S�cakl�k: {temperature:F1}�C, dP/dt: {pressureRate:F3} hPa/s");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ArduinoPage: SettingPage veri i�leme hatas�: {ex.Message}");
            }
        }
    }
}