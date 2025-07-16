// HYI.xaml.cs
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO.Ports;
using System.Linq;
using Windows.UI;

namespace copilot_deneme
{
    public sealed partial class HYI : Page
    {
        private DispatcherQueue _dispatcherQueue;

        // �ki ayr� port y�neticisi
        private SerialPortServiceHYI _inputPortManager;// Arduino'dan veri almak i�in
        private SerialPortServiceHYI _outputPortManager; // Veriyi hedefe g�ndermek i�in

        private ushort _packetCounter = 0; // Giden paketler i�in saya�

        public HYI()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // Port y�neticilerini ba�lat
            _inputPortManager = new SerialPortServiceHYI(_dispatcherQueue);
            _outputPortManager = new SerialPortServiceHYI(_dispatcherQueue);

            // Arduino'dan do�rulanm�� bir paket geldi�inde tetiklenecek olay� (event) ba�l�yoruz.
            _inputPortManager.PacketReceived += OnArduinoPacketReceived;

            // Sayfa kapat�ld���nda portlar� serbest b�rakmak i�in
            this.Unloaded += (s, e) =>
            {
                _inputPortManager.Stop();
                _outputPortManager.Stop();
            };

            RefreshAvailablePorts();
        }

        /// <summary>
        /// Arduino'dan tam ve do�rulanm�� bir payload paketi geldi�inde bu metot �al���r.
        /// </summary>
        /// <param name="arduinoPayload">Arduino'dan gelen 44 byte'l�k ham sens�r verisi.</param>
        private void OnArduinoPacketReceived(byte[] arduinoPayload)
        {
            System.Diagnostics.Debug.WriteLine($"��leniyor: {arduinoPayload.Length} byte'l�k Arduino payload'u.");

            // Veriyi sadece ��k�� portu a��ksa g�nder
            if (!_outputPortManager.IsOpen)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText_Output.Text = "��k�� portu kapal�, veri g�nderilemiyor.";
                    StatusText_Output.Foreground = new SolidColorBrush(Colors.OrangeRed);
                });
                return;
            }

            try
            {
                // Giden paket i�in meta verileri olu�tur
                var metaData = new HYITelemetryData
                {
                    TeamId = 0x54, // �rnek Tak�m ID'si
                    PacketCounter = (byte)(_packetCounter++ % 256),
                    Status = 0x01 // �rnek durum: U�u�ta
                };

                // Arduino'dan gelen ham payload'u ve meta veriyi kullanarak 78 byte'l�k paketi olu�tur.
                byte[] packetToSend = HYIDataPacket(metaData, arduinoPayload);

                // Paketi ��k�� portundan g�nder.
                _outputPortManager.Write(packetToSend);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText_Output.Text = $"{_packetCounter}. paket g�nderildi.";
                    StatusText_Output.Foreground = new SolidColorBrush(Colors.Green);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Paket g�nderme hatas�: {ex.Message}");
                _dispatcherQueue.TryEnqueue(() =>
                {
                    StatusText_Output.Text = "Paket g�nderme hatas�!";
                    StatusText_Output.Foreground = new SolidColorBrush(Colors.Red);
                });
            }
        }


        private void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            RefreshAvailablePorts();
        }

        private void RefreshAvailablePorts()
        {
            string[] availablePorts = SerialPort.GetPortNames();
            var inputSelection = PortComboBox_Input.SelectedItem as string;
            var outputSelection = PortComboBox_Output.SelectedItem as string;

            PortComboBox_Input.ItemsSource = availablePorts;
            PortComboBox_Output.ItemsSource = availablePorts;

            if (availablePorts.Contains(inputSelection)) PortComboBox_Input.SelectedItem = inputSelection;
            else if (availablePorts.Length > 0) PortComboBox_Input.SelectedIndex = 0;

            if (availablePorts.Contains(outputSelection)) PortComboBox_Output.SelectedItem = outputSelection;
            else if (availablePorts.Length > 1) PortComboBox_Output.SelectedIndex = 1;
            else if (availablePorts.Length > 0) PortComboBox_Output.SelectedIndex = 0;

            StatusText_Input.Text = $"{availablePorts.Length} port bulundu.";
        }

        private void ConnectInputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var portName = PortComboBox_Input.SelectedItem as string;
                if (string.IsNullOrEmpty(portName)) throw new InvalidOperationException("Giri� i�in bir port se�in.");
                var baudRate = int.Parse((BaudRateComboBox_Input.SelectedItem as ComboBoxItem).Content.ToString());

                _inputPortManager.Initialize(portName, baudRate);
                _inputPortManager.Start();

                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.LightGreen);
                StatusText_Input.Text = $"Ba�land�: {portName}";
            }
            catch (Exception ex)
            {
                StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Input.Text = $"Hata: {ex.Message}";
            }
        }

        private void DisconnectInputPort_Click(object sender, RoutedEventArgs e)
        {
            _inputPortManager.Stop();
            StatusIndicator_Input.Fill = new SolidColorBrush(Colors.Red);
            StatusText_Input.Text = "Giri� Portu Kapal�";
        }

        private void ConnectOutputPort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var portName = PortComboBox_Output.SelectedItem as string;
                if (string.IsNullOrEmpty(portName)) throw new InvalidOperationException("��k�� i�in bir port se�in.");
                var baudRate = int.Parse((BaudRateComboBox_Output.SelectedItem as ComboBoxItem).Content.ToString());

                _outputPortManager.Initialize(portName, baudRate);
                _outputPortManager.Start(); // Portu sadece yazma i�lemi i�in de olsa a�mam�z gerekir.

                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.LightGreen);
                StatusText_Output.Text = $"Ba�land�: {portName}";
            }
            catch (Exception ex)
            {
                StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Red);
                StatusText_Output.Text = $"Hata: {ex.Message}";
            }
        }

        private void DisconnectOutputPort_Click(object sender, RoutedEventArgs e)
        {
            _outputPortManager.Stop();
            StatusIndicator_Output.Fill = new SolidColorBrush(Colors.Red);
            StatusText_Output.Text = "��k�� Portu Kapal�";
        }

       

        /// <summary>
        /// Meta verileri ve Arduino'dan gelen ham payload'u birle�tirerek standart 78 byte'l�k paketi olu�turur.
        /// </summary>
         static byte[] HYIDataPacket(HYITelemetryData metaData, byte[] arduinoPayload)
        {
            byte[] packet = new byte[78];
            packet[0] = 0xFF; // Start Byte 1
            packet[1] = 0xFF; // Start Byte 2
            packet[2] = 0x54; // Header 1
            packet[3] = 0x52; // Header 2
            packet[4] = metaData.TeamId;
            packet[5] = metaData.PacketCounter;

            // Arduino'dan gelen 44 byte'l�k ham veriyi (11 float) do�rudan paketin do�ru yerlerine kopyala.
            // Bu y�ntem, string parse etmekten �ok daha h�zl� ve g�venilirdir.

            // Arduino Payload S�ras�: Altitude(4), GpsAlt(4), Lat(4), Lon(4), GyroX(4)... (Toplam 11 float)
            // Hedef Paket Konumlar�: Altitude->6, GpsAlt->10, Lat->14, Lon->18, ..., GyroX->46, ...

            Array.Copy(arduinoPayload, 0, packet, 6, 4);   // Altitude (irtifa)
            Array.Copy(arduinoPayload, 4, packet, 10, 4);  // RocketGpsAltitude
            Array.Copy(arduinoPayload, 8, packet, 14, 4);  // RocketLatitude
            Array.Copy(arduinoPayload, 12, packet, 18, 4); // RocketLongitude

            // Payload GPS verileri bu �rnekte Arduino'dan g�nderilmedi�i varsay�ld�.
            // E�er g�nderiliyorsa, arduinoPayload'daki do�ru ofsetlerden kopyalanmal�d�r.
            // �rn: Array.Copy(arduinoPayload, 16, packet, 22, 4); // PayloadGPSAltitude

            Array.Copy(arduinoPayload, 16, packet, 46, 4); // GyroscopeX
            Array.Copy(arduinoPayload, 20, packet, 50, 4); // GyroscopeY
            Array.Copy(arduinoPayload, 24, packet, 54, 4); // GyroscopeZ
            Array.Copy(arduinoPayload, 28, packet, 58, 4); // AccelerationX
            Array.Copy(arduinoPayload, 32, packet, 62, 4); // AccelerationY
            Array.Copy(arduinoPayload, 36, packet, 66, 4); // AccelerationZ
            Array.Copy(arduinoPayload, 40, packet, 70, 4); // Angle

            packet[74] = metaData.Status;
            packet[75] = CheckSum(packet); // Checksum hesapla
            packet[76] = 0x0D; // End Byte 1
            packet[77] = 0x0A; // End Byte 2
            return packet;
        }

        private static byte CheckSum(byte[] data)
        {
            int sum = 0;
            // Kurala g�re 4. ve 74. byte'lar (dahil) aras� toplan�r.
            for (int i = 4; i <= 74; i++)
            {
                sum += data[i];
            }
            return (byte)(sum % 256);
        }

      
    }
}