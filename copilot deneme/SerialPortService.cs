using System;
using System.IO.Ports;
using System.Threading;
using System.Windows;
using Microsoft.UI.Dispatching;
using copilot_deneme.ViewModels;

namespace copilot_deneme
{
    public static class SerialPortService
    {
        private static SerialPort _serialPort;
        public static SerialPort SerialPort
        {
            get => _serialPort;
            set => _serialPort = value;
        }

        public static ChartViewModel ViewModel { get; set; }
        public static DispatcherQueue Dispatcher { get; set; }

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

            if (!SerialPort.IsOpen)
            {
                SerialPort.DataReceived += SerialPort_DataReceived;
                SerialPort.Open();
            }
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
                string line = SerialPort.ReadLine();
                System.Diagnostics.Debug.WriteLine($"Raw Serial: '{line}'");

                // Bo� sat�r kontrol�
                if (string.IsNullOrWhiteSpace(line))
                {
                    System.Diagnostics.Debug.WriteLine("Received empty line");
                    return;
                }

                // Her sat�r�n i�eri�ini detayl� incele
                System.Diagnostics.Debug.WriteLine($"Line length: {line.Length}");
                System.Diagnostics.Debug.WriteLine($"Line bytes: {string.Join(",", System.Text.Encoding.ASCII.GetBytes(line))}");

                // Arduino'dan gelen her sat�r� ayr� ayr� i�le
                if (line.StartsWith("LDR:"))
                {
                    string ldrValueStr = line.Substring(4).Trim();
                    System.Diagnostics.Debug.WriteLine($"Attempting to parse LDR value: '{ldrValueStr}'");

                    if (double.TryParse(ldrValueStr, out double ldr) && ldr != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Successfully parsed LDR value: {ldr}");
                        Dispatcher?.TryEnqueue(() =>
                        {
                            System.Diagnostics.Debug.WriteLine($"About to add LDR value to ViewModel: {ldr}");
                            System.Diagnostics.Debug.WriteLine($"ViewModel is null: {ViewModel == null}");
                            System.Diagnostics.Debug.WriteLine($"Dispatcher is null: {Dispatcher == null}");

                            ViewModel?.AddLdrValue(ldr);
                            System.Diagnostics.Debug.WriteLine($"Added LDR value to chart: {ldr}");
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse LDR value from: '{ldrValueStr}'");
                    }
                }
                // Hem "DIST:" hem de ";DIST:" ile ba�layan sat�rlar� kontrol et
                else if (line.StartsWith("DIST:") || line.StartsWith(";DIST:"))
                {
                    string distValueStr;
                    if (line.StartsWith(";DIST:"))
                    {
                        distValueStr = line.Substring(6).Trim(); // ";DIST:" 6 karakter
                    }
                    else
                    {
                        distValueStr = line.Substring(5).Trim(); // "DIST:" 5 karakter
                    }

                    System.Diagnostics.Debug.WriteLine($"Attempting to parse DIST value: '{distValueStr}'");

                    if (double.TryParse(
                        distValueStr,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double distance) && distance != 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"Successfully parsed DIST value: {distance}");
                        Dispatcher?.TryEnqueue(() =>
                        {
                            System.Diagnostics.Debug.WriteLine($"About to add DIST value to ViewModel: {distance}");
                            System.Diagnostics.Debug.WriteLine($"ViewModel is null: {ViewModel == null}");
                            System.Diagnostics.Debug.WriteLine($"Dispatcher is null: {Dispatcher == null}");

                            ViewModel?.AddDistanceValue(distance);
                            System.Diagnostics.Debug.WriteLine($"Added DIST value to chart: {distance}");
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse DIST value from: '{distValueStr}'");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Line doesn't match expected format -> '{line}'");

                    // E�er sadece say� ise (mesela ";DIST:" sat�r�ndan sonra gelen distance de�eri)
                    if (double.TryParse(line.Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double numericValue))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found standalone numeric value: {numericValue}");
                        // Bu de�erin distance oldu�unu varsay�yoruz (Arduino koduna g�re)
                        Dispatcher?.TryEnqueue(() =>
                        {
                            System.Diagnostics.Debug.WriteLine($"About to add standalone numeric value: {numericValue}");
                            System.Diagnostics.Debug.WriteLine($"ViewModel is null: {ViewModel == null}");
                            System.Diagnostics.Debug.WriteLine($"Dispatcher is null: {Dispatcher == null}");

                            ViewModel?.AddDistanceValue(numericValue);
                            System.Diagnostics.Debug.WriteLine($"Added standalone numeric value as distance: {numericValue}");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SerialPort_DataReceived: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // Port durumunu kontrol etmek i�in yard�mc� metot
        public static bool IsPortOpen()
        {
            return SerialPort?.IsOpen == true;
        }

        // Port bilgilerini almak i�in yard�mc� metot
        public static string GetPortInfo()
        {
            if (SerialPort == null)
                return "Port not initialized";

            return $"Port: {SerialPort.PortName}, BaudRate: {SerialPort.BaudRate}, IsOpen: {SerialPort.IsOpen}";
        }

        // Kaynaklar� temizlemek i�in
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
    }
}