using System;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

namespace copilot_deneme
{
    /// <summary>
    /// Sadece roket verilerini debug etmek i�in basit test s�n�f�
    /// </summary>
    public class RocketDebugTool
    {
        private SerialPortService? _serialPortService;

        public async Task StartDebugAsync(string portName, int baudRate = 9600)
        {
            try
            {
                Console.WriteLine("?? ROKET DEBUG ARACI BA�LATILIYOR");
                Console.WriteLine("================================");
                Console.WriteLine($"Port: {portName}");
                Console.WriteLine($"Baud Rate: {baudRate}");
                Console.WriteLine("Sadece roket verisi dinleniyor...");
                Console.WriteLine("");

                _serialPortService = new SerialPortService();

                // Sadece roket verisini dinle
                _serialPortService.OnRocketDataUpdated += OnRocketDataReceived;
                _serialPortService.OnError += OnErrorReceived;

                // Debug i�in ham veriyi de dinle
                _serialPortService.OnDataReceived += OnRawDataReceived;

                await _serialPortService.InitializeAsync(portName, baudRate);
                await _serialPortService.StartReadingAsync();

                Console.WriteLine("? Debug ba�lat�ld�. Roket verileri bekleniyor...");
                Console.WriteLine("��kmak i�in 'q' tu�una bas�n\n");

                // Kullan�c� 'q' tu�una basana kadar bekle
                ConsoleKeyInfo keyInfo;
                do
                {
                    keyInfo = Console.ReadKey(true);
                } while (keyInfo.Key != ConsoleKey.Q);

                await StopDebugAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Debug ba�latma hatas�: {ex.Message}");
                Debug.WriteLine($"RocketDebugTool hata: {ex}");
            }
        }

        private void OnRocketDataReceived(SerialPortService.RocketTelemetryData rocketData)
        {
            try
            {
                Console.WriteLine($"?? [{DateTime.Now:HH:mm:ss.fff}] ROKET VER�S� ALINDI:");
                Console.WriteLine($"   ? Paket No: {rocketData.PacketCounter}");
                Console.WriteLine($"   ? �rtifa: {rocketData.RocketAltitude:F2} m");
                Console.WriteLine($"   ? GPS �rtifa: {rocketData.RocketGpsAltitude:F2} m");
                Console.WriteLine($"   ? Konum: {rocketData.RocketLatitude:F6}, {rocketData.RocketLongitude:F6}");
                Console.WriteLine($"   ? H�z: {rocketData.RocketSpeed:F2} m/s");
                Console.WriteLine($"   ? S�cakl�k: {rocketData.RocketTemperature:F1} �C");
                Console.WriteLine($"   ? Bas�n�: {rocketData.RocketPressure:F1} hPa");
                Console.WriteLine($"   ? Durum: {rocketData.status}");
                Console.WriteLine($"   ? CRC: 0x{rocketData.CRC:X2}");
                Console.WriteLine($"   ? Team ID: {rocketData.TeamID}");
                Console.WriteLine("   " + new string('=', 50));

                Debug.WriteLine($"ROKET DEBUG: Paket #{rocketData.PacketCounter}, �rtifa: {rocketData.RocketAltitude:F2}m");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Roket verisi i�leme hatas�: {ex.Message}");
                Debug.WriteLine($"Roket data debug hatas�: {ex}");
            }
        }

        private void OnRawDataReceived(string rawData)
        {
            try
            {
                // Ham veriyi hex olarak g�ster (ilk 50 karakter)
                string displayData = rawData.Length > 50 ? rawData.Substring(0, 50) + "..." : rawData;
                
                // Sadece binary veri varsa hex'e �evir
                bool hasBinaryData = false;
                foreach (char c in rawData)
                {
                    if (char.IsControl(c) && c != '\r' && c != '\n')
                    {
                        hasBinaryData = true;
                        break;
                    }
                }

                if (hasBinaryData)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(rawData);
                    int maxBytes = Math.Min(20, bytes.Length);
                    string[] hexArray = new string[maxBytes];
                    for (int i = 0; i < maxBytes; i++)
                    {
                        hexArray[i] = bytes[i].ToString("X2");
                    }
                    string hex = string.Join(" ", hexArray);
                    Console.WriteLine($"?? [{DateTime.Now:HH:mm:ss.fff}] Ham veri (hex): {hex}...");
                }

                Debug.WriteLine($"Ham veri al�nd�: {rawData.Length} byte");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ham veri i�leme hatas�: {ex}");
            }
        }

        private void OnErrorReceived(string errorMessage)
        {
            Console.WriteLine($"??  [{DateTime.Now:HH:mm:ss}] HATA: {errorMessage}");
            Debug.WriteLine($"SerialPort hatas�: {errorMessage}");
        }

        public async Task StopDebugAsync()
        {
            try
            {
                if (_serialPortService != null)
                {
                    Console.WriteLine("\n?? Debug durduruluyor...");

                    _serialPortService.OnRocketDataUpdated -= OnRocketDataReceived;
                    _serialPortService.OnError -= OnErrorReceived;
                    _serialPortService.OnDataReceived -= OnRawDataReceived;

                    await _serialPortService.StopReadingAsync();
                    await _serialPortService.DisposeAsync();

                    Console.WriteLine("? Debug ba�ar�yla durduruldu");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Debug durdurma hatas�: {ex.Message}");
                Debug.WriteLine($"Debug stop hatas�: {ex}");
            }
        }

        /// <summary>
        /// H�zl� test metodu
        /// </summary>
        public static async Task QuickTestAsync(string portName = "COM3")
        {
            var debug = new RocketDebugTool();
            await debug.StartDebugAsync(portName);
        }

        /// <summary>
        /// Ana metod - konsol uygulamas� olarak �al��t�r�labilir
        /// </summary>
        public static async Task Main(string[] args)
        {
            try
            {
                Console.Title = "Roket Debug Arac�";
                Console.WriteLine("?? ROKET TELEMETRI DEBUG ARACI");
                Console.WriteLine("==============================");

                string portName = args.Length > 0 ? args[0] : "COM3";
                int baudRate = args.Length > 1 && int.TryParse(args[1], out int br) ? br : 9600;

                Console.WriteLine($"Kullan�m: RocketDebugTool.exe {portName} {baudRate}");
                Console.WriteLine($"Mevcut ayarlar: Port={portName}, BaudRate={baudRate}\n");

                await QuickTestAsync(portName);

                Console.WriteLine("\n?? Debug tamamland�. Kapat�l�yor...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? Kritik hata: {ex.Message}");
                Debug.WriteLine($"Main method hatas�: {ex}");
            }
        }
    }
}