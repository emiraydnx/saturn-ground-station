using System;
using System.IO.Ports;
using System.Threading;
using System.Windows;
using Microsoft.UI.Dispatching;
using copilot_deneme.ViewModels;
using System.Globalization;

namespace copilot_deneme
{
    public static class SerialPortService
    {
        private static SerialPort _serialPort;
        private static string _buffer = string.Empty;
        
        public static SerialPort SerialPort             
        {
            get => _serialPort;
            set => _serialPort = value;
        }

        public static ChartViewModel ViewModel { get; set; }
        public static DispatcherQueue Dispatcher { get; set; }
        
        // Yeni event handler - ham veri i�in
        public static event Action<string> OnDataReceived;

        public static void Initialize(string portName, int baudRate)
        {
            System.Diagnostics.Debug.WriteLine($"Initializing SerialPort with port:{portName}, baudRate:{baudRate}");
            SerialPort = new SerialPort
            {
                PortName = portName,
                BaudRate = baudRate,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One
            };
            System.Diagnostics.Debug.WriteLine("SerialPort initialized");
        }

        public static void StartReading()
        {
            if (SerialPort == null)
                throw new InvalidOperationException("Serial port must be initialized before starting");

            // Event handler'� her zaman ekle (duplicate kontrol� yap)
            SerialPort.DataReceived -= SerialPort_DataReceived; // �nce kald�r
            SerialPort.DataReceived += SerialPort_DataReceived; // Sonra ekle
            
            if (!SerialPort.IsOpen)
            {
                SerialPort.Open();
            }
            
            System.Diagnostics.Debug.WriteLine($"StartReading called - Port open: {SerialPort.IsOpen}, Event handler added");
        }

        public static void StopReading()
        {
            if (SerialPort?.IsOpen == true)
            {
                SerialPort.DataReceived -= SerialPort_DataReceived;
                SerialPort.Close();
            }
        }

        private static void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // Mevcut buffer'daki t�m veriyi oku
                string availableData = SerialPort.ReadExisting();
                System.Diagnostics.Debug.WriteLine($"Raw Serial Data: '{availableData}'");

                // Ham veriyi event ile g�nder
                OnDataReceived?.Invoke(availableData);

                // Chart verilerini i�le (comma-separated format)
                ProcessArduinoData(availableData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SerialPort_DataReceived: {ex.Message}");
                OnDataReceived?.Invoke($"ERROR: {ex.Message}");
            }
        }

        private static void ProcessArduinoData(string receivedData)
        {
            try
            {
                _buffer += receivedData;

                // Sat�rlar� ay�r
                string[] lines = _buffer.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                // Son sat�r eksik olabilir, onu buffer'da b�rak
                if (!_buffer.EndsWith('\n') && !_buffer.EndsWith('\r') && lines.Length > 0)
                {
                    _buffer = lines[lines.Length - 1];
                    Array.Resize(ref lines, lines.Length - 1);
                }
                else
                {
                    _buffer = string.Empty;
                }

                foreach (string line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        ProcessDataLine(line.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ProcessArduinoData: {ex.Message}");
            }
        }

        private static void ProcessDataLine(string data)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Processing line: '{data}'");
                
                // �nce key:value format�n� kontrol et
                if (data.Contains(':'))
                {
                    ProcessKeyValueData(data);
                    return;
                }

                // Comma-separated format�n� i�le
                string[] parcalar = data.Split(',');
                
                if (parcalar.Length < 16)
                {
                    System.Diagnostics.Debug.WriteLine($"Beklenen 16 veri, al�nan: {parcalar.Length}");
                    return;
                }

                // InvariantCulture kullan - nokta (.) ondal�k ay�r�c� olarak
                var culture = CultureInfo.InvariantCulture;

                // Arduino'dan gelen veri s�ras�na g�re parse et
                ushort paketSayaci = (ushort)TryParseFloat(parcalar[0], culture, 0);      // 0: paketSayaci
                float irtifa = TryParseFloat(parcalar[1], culture, 0);                     // 1: irtifa (Roket �rtifa)
                float roketGpsIrtifa = TryParseFloat(parcalar[2], culture, 0);             // 2: roketGpsIrtifa
                float roketGpsEnlem = TryParseFloat(parcalar[3], culture, 0);              // 3: roketGpsEnlem
                float roketGpsBoylam = TryParseFloat(parcalar[4], culture, 0);             // 4: roketGpsBoylam
                float payloadGpsIrtifa = TryParseFloat(parcalar[5], culture, 0);           // 5: payloadGpsIrtifa (Payload �rtifa)
                float payloadGpsEnlem = TryParseFloat(parcalar[6], culture, 0);            // 6: payloadGpsEnlem
                float payloadGpsBoylam = TryParseFloat(parcalar[7], culture, 0);           // 7: payloadGpsBoylam
                float jiroskopX = TryParseFloat(parcalar[8], culture, 0);                  // 8: jiroskopX
                float jiroskopY = TryParseFloat(parcalar[9], culture, 0);                  // 9: jiroskopY
                float jiroskopZ = TryParseFloat(parcalar[10], culture, 0);                 // 10: jiroskopZ
                float ivmeX = TryParseFloat(parcalar[11], culture, 0);                     // 11: ivmeX
                float ivmeY = TryParseFloat(parcalar[12], culture, 0);                     // 12: ivmeY
                float ivmeZ = TryParseFloat(parcalar[13], culture, 0);                     // 13: ivmeZ (Z �vmesi)
                float aci = TryParseFloat(parcalar[14], culture, 0);                       // 14: aci
                byte durum = (byte)TryParseFloat(parcalar[15], culture, 0);                // 15: durum

                System.Diagnostics.Debug.WriteLine($"Successfully parsed - Roket �rtifa: {irtifa}, Payload �rtifa: {payloadGpsIrtifa}, Z �vmesi: {ivmeZ}");

                // Chart'lara do�ru veri s�ras�yla ekle
                UpdateCharts(irtifa, roketGpsIrtifa, payloadGpsIrtifa, ivmeZ, 
                           jiroskopX, jiroskopY, jiroskopZ, ivmeX, ivmeY, aci, durum, paketSayaci);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing Arduino data: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Raw data was: '{data}'");
            }
        }

        private static void ProcessKeyValueData(string data)
        {
            var parts = data.Split(':', 2);
            if (parts.Length != 2)
                return;

            string key = parts[0].Trim();
            string valueStr = parts[1].Trim();

            if (!float.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsedValue))
                return;

            Dispatcher?.TryEnqueue(() =>
            {
                switch (key)
                {
                    case "rocketAltitude":
                        ViewModel?.AddRocketAltitudeValue(parsedValue);
                        break;
                    case "payloadAltitude":
                        ViewModel?.addPayloadAltitudeValue(parsedValue);
                        break;
                    case "rocketAccelZ":
                        ViewModel?.addRocketAccelZValue(parsedValue);
                        break;
                    case "payloadAccelZ":
                        ViewModel?.addPayloadAccelZValue(parsedValue);
                        break;
                    case "rocketSpeed":
                        ViewModel?.addRocketSpeedValue(parsedValue);
                        break;
                    case "payloadSpeed":
                        ViewModel?.addPayloadSpeedValue(parsedValue);
                        break;
                    case "rocketTemperature":
                        ViewModel?.addRocketTempValue(parsedValue);
                        break;
                    case "payloadTemperature":
                        ViewModel?.addPayloadTempValue(parsedValue);
                        break;
                    case "rocketPressure":
                        ViewModel?.addRocketPressureValue(parsedValue);
                        break;
                    case "payloadPressure":
                        ViewModel?.addPayloadPressureValue(parsedValue);
                        break;
                    case "payloadHumidity":
                        ViewModel?.addPayloadHumidityValue(parsedValue);
                        break;
                    default:
                        System.Diagnostics.Debug.WriteLine($"Bilinmeyen anahtar al�nd�: {key}");
                        break;
                }
            });
        }

        private static void UpdateCharts(float roketIrtifa, float roketGpsIrtifa, float payloadIrtifa, 
            float ivmeZ, float jiroskopX, float jiroskopY, float jiroskopZ, 
            float ivmeX, float ivmeY, float aci, byte durum, ushort paketSayaci)
        {
            Dispatcher?.TryEnqueue(() =>
            {
                try
                {
                    if (ViewModel != null)
                    {
                        // Direkt sens�r verilerini chart'lara ekle - hesaplama yok
                        ViewModel.AddRocketAltitudeValue(roketIrtifa);        // Roket irtifa sens�r�
                        ViewModel.addPayloadAltitudeValue(payloadIrtifa);     // Payload irtifa sens�r�
                        
                        ViewModel.addRocketAccelZValue(ivmeZ);                // Roket Z ivme sens�r�
                        ViewModel.addPayloadAccelZValue(ivmeZ);               // Payload Z ivme sens�r� (ayn� sens�r)
                        
                        // Di�er sens�r verileri - direkt olarak
                        ViewModel.addRocketSpeedValue(0);                     // H�z sens�r� yok, 0
                        ViewModel.addPayloadSpeedValue(0);                    // H�z sens�r� yok, 0
                        
                        ViewModel.addRocketTempValue(25);                     // S�cakl�k sens�r� yok, sabit de�er
                        ViewModel.addPayloadTempValue(25);                    // S�cakl�k sens�r� yok, sabit de�er
                        
                        ViewModel.addRocketPressureValue(1013);               // Bas�n� sens�r� yok, deniz seviyesi
                        ViewModel.addPayloadPressureValue(1013);              // Bas�n� sens�r� yok, deniz seviyesi
                        
                        ViewModel.addPayloadHumidityValue(50);                // Nem sens�r� yok, sabit de�er
                        
                        ViewModel.UpdateStatus($"Sens�r verisi: {DateTime.Now:HH:mm:ss} - Paket: {paketSayaci}");
                        
                        // T�m sens�r verilerini debug'a yazd�r
                        System.Diagnostics.Debug.WriteLine($"SENS�R VER�LER� - Paket: {paketSayaci}");
                        System.Diagnostics.Debug.WriteLine($"  Roket �rtifa: {roketIrtifa:F2} m");
                        System.Diagnostics.Debug.WriteLine($"  Roket GPS �rtifa: {roketGpsIrtifa:F2} m");
                        System.Diagnostics.Debug.WriteLine($"  Payload GPS �rtifa: {payloadIrtifa:F2} m");
                        System.Diagnostics.Debug.WriteLine($"  Jiroskop X: {jiroskopX:F2}, Y: {jiroskopY:F2}, Z: {jiroskopZ:F2}");
                        System.Diagnostics.Debug.WriteLine($"  �vme X: {ivmeX:F2}, Y: {ivmeY:F2}, Z: {ivmeZ:F2}");
                        System.Diagnostics.Debug.WriteLine($"  A��: {aci:F2}�, Durum: {durum}");
                        System.Diagnostics.Debug.WriteLine("---");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("ViewModel is null!");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating charts: {ex.Message}");
                }
            });
        }

        private static float TryParseFloat(string value, CultureInfo culture, float defaultValue)
        {
            if (float.TryParse(value?.Trim(), culture, out float result))
                return result;
            return defaultValue;
        }

        public static bool IsPortOpen()
        {
            return SerialPort?.IsOpen == true;
        }

        public static string GetPortInfo()
        {
            if (SerialPort == null)
                return "Port not initialized";

            return $"Port: {SerialPort.PortName}, BaudRate: {SerialPort.BaudRate}, IsOpen: {SerialPort.IsOpen}";
        }

        public static void Dispose()
        {
            try
            {
                StopReading();
                SerialPort?.Dispose();
                SerialPort = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing SerialPort: {ex.Message}");
            }
        }

        // Test i�in public metod
        public static void TriggerTestData(string testData)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Triggering test data: {testData}");
                OnDataReceived?.Invoke(testData);
                ProcessArduinoData(testData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error triggering test data: {ex.Message}");
            }
        }
    }
}