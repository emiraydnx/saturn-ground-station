using copilot_deneme.TelemetryData;
using copilot_deneme.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static copilot_deneme.TelemetryData.TelemetryConstants;

namespace copilot_deneme.Services
{
    /// <summary>
    /// Telemetri paket iþlemcisi - Ham binary verileri telemetri data sýnýflarýna dönüþtürür
    /// SerialPortService'den ayrýlan paket iþleme sorumluluðu
    /// </summary>
    public class TelemetryPacketProcessor : ITelemetryPacketProcessor
    {
        private readonly object _bufferLock = new object();
        private readonly List<byte> _binaryBuffer = new List<byte>();

        #region Events
        public event Action<RocketTelemetryData>? OnRocketDataParsed;
        public event Action<PayloadTelemetryData>? OnPayloadDataParsed;
        public event Action<HYITelemetryData>? OnHYIDataParsed;
        public event Action<HYIDenemeData>? OnHYIDenemeDataParsed;
        public event Action<string>? OnParsingError;
        #endregion

        #region Public Methods
        /// <summary>
        /// Ham binary veriyi buffer'a ekler ve paket iþleme yapar
        /// </summary>
        public void ProcessBinaryData(byte[] data)
        {
            try
            {
                lock (_bufferLock)
                {
                    _binaryBuffer.AddRange(data);

                    // Buffer overflow korumasý
                    if (_binaryBuffer.Count > MAX_BUFFER_SIZE)
                    {
                        var removeCount = _binaryBuffer.Count - MAX_BUFFER_SIZE;
                        _binaryBuffer.RemoveRange(0, removeCount);
                        OnParsingError?.Invoke($"Buffer overflow, {removeCount} byte silindi");
                    }
                }

                // Paket iþleme
                ProcessPacketsInBuffer();
            }
            catch (Exception ex)
            {
                OnParsingError?.Invoke($"Binary veri iþleme hatasý: {ex.Message}");
            }
        }

        /// <summary>
        /// Buffer'ý temizler
        /// </summary>
        public void ClearBuffer()
        {
            lock (_bufferLock)
            {
                _binaryBuffer.Clear();
            }
        }

        /// <summary>
        /// Buffer durumu hakkýnda bilgi verir
        /// </summary>
        public string GetBufferStatus()
        {
            lock (_bufferLock)
            {
                return $"Buffer: {_binaryBuffer.Count} byte";
            }
        }
        #endregion

        #region Private Packet Processing Methods
        /// <summary>
        /// Buffer'daki paketleri iþler
        /// </summary>
        private void ProcessPacketsInBuffer()
        {
            try
            {
                lock (_bufferLock)
                {
                    Debug.WriteLine($"Buffer iþleniyor: {_binaryBuffer.Count} byte mevcut");

                    // Paket türlerini sýrayla iþle
                    ProcessRocketTelemetryPackets();
                    ProcessPayloadTelemetryPackets();
                    ProcessHYIPackets();
                    ProcessHYIDenemePackets();
                }
            }
            catch (Exception ex)
            {
                OnParsingError?.Invoke($"Paket iþleme hatasý: {ex.Message}");
            }
        }

        /// <summary>
        /// Roket telemetri paketlerini iþler
        /// </summary>
        private void ProcessRocketTelemetryPackets()
        {
            ProcessPackets(ROCKET_HEADER, ROCKET_PACKET_SIZE, ParseRocketData,
                data => {
                    Debug.WriteLine($"Roket paketi parse edildi: #{data.PacketCounter}, Ýrtifa: {data.RocketAltitude:F2}m");
                    OnRocketDataParsed?.Invoke(data);
                }, "Rocket");
        }

        /// <summary>
        /// Payload telemetri paketlerini iþler
        /// </summary>
        private void ProcessPayloadTelemetryPackets()
        {
            ProcessPackets(PAYLOAD_HEADER, PAYLOAD_PACKET_SIZE, ParsePayloadData,
                data => {
                    Debug.WriteLine($"Payload paketi parse edildi: #{data.PacketCounter}, Ýrtifa: {data.PayloadAltitude:F2}m");
                    OnPayloadDataParsed?.Invoke(data);
                }, "Payload");
        }

        /// <summary>
        /// HYI paketlerini iþler
        /// </summary>
        private void ProcessHYIPackets()
        {
            ProcessPackets(HYI_HEADER, HYI_PACKET_SIZE, ParseHYIData,
                data => {
                    Debug.WriteLine($"HYI paketi parse edildi: #{data.PacketCounter}, Ýrtifa: {data.Altitude:F2}m");
                    OnHYIDataParsed?.Invoke(data);
                }, "HYI");
        }

        /// <summary>
        /// HYIDenem paketlerini iþler
        /// </summary>
        private void ProcessHYIDenemePackets()
        {
            ProcessPackets(HYIDENEM_HEADER, HYIDENEM_PACKET_SIZE, ParseHYIDenemeData,
                data => {
                    Debug.WriteLine($"HYIDenem paketi parse edildi: #{data.PacketCounter}, Roket: {data.RocketAltitude:F2}m");
                    OnHYIDenemeDataParsed?.Invoke(data);
                }, "HYIDenem");
        }

        /// <summary>
        /// Generic paket iþleyici
        /// </summary>
        private void ProcessPackets<T>(byte[] header, int packetSize,
            Func<byte[], T?> parser, Action<T> onDataReceived, string packetType) where T : class
        {
            while (_binaryBuffer.Count >= packetSize)
            {
                int headerIndex = FindHeader(_binaryBuffer, header);

                if (headerIndex == -1)
                {
                    // Header bulunamadý, buffer'dan bir byte sil
                    if (_binaryBuffer.Count > header.Length)
                    {
                        _binaryBuffer.RemoveAt(0);
                        Debug.WriteLine($"{packetType} header bulunamadý, 1 byte silindi. Kalan: {_binaryBuffer.Count}");
                    }
                    else
                        break;
                    continue;
                }

                if (headerIndex > 0)
                {
                    // Header baþlangýçta deðil, önceki verileri sil
                    Debug.WriteLine($"{packetType} Header {headerIndex} pozisyonunda, önceki {headerIndex} byte siliniyor");
                    _binaryBuffer.RemoveRange(0, headerIndex);
                    continue;
                }

                // Tam paket var mý kontrol et
                if (_binaryBuffer.Count < packetSize)
                {
                    Debug.WriteLine($"{packetType} için yetersiz veri: {_binaryBuffer.Count}/{packetSize} byte");
                    break;
                }

                // Paketi parse et
                byte[] packet = _binaryBuffer.GetRange(0, packetSize).ToArray();
                var telemetryData = parser(packet);

                if (telemetryData != null)
                {
                    onDataReceived(telemetryData);
                }
                else
                {
                    OnParsingError?.Invoke($"{packetType} paketi parse edilemedi");
                }

                _binaryBuffer.RemoveRange(0, packetSize);
            }
        }
        #endregion

        #region Packet Parsing Methods
        /// <summary>
        /// Roket paketini parse eder
        /// </summary>
        private static RocketTelemetryData? ParseRocketData(byte[] packet)
        {
            try
            {
                if (!IsValidPacket(packet, ROCKET_HEADER))
                {
                    Debug.WriteLine("? Roket paketi header validation baþarýsýz!");
                    return null;
                }

                var data = new RocketTelemetryData
                {
                    PacketCounter = packet[4],
                    RocketAltitude = BitConverter.ToSingle(packet, 5),
                    RocketGpsAltitude = BitConverter.ToSingle(packet, 9),
                    RocketLatitude = BitConverter.ToSingle(packet, 13),
                    RocketLongitude = BitConverter.ToSingle(packet, 17),
                    GyroX = BitConverter.ToSingle(packet, 21),
                    GyroY = BitConverter.ToSingle(packet, 25),
                    GyroZ = BitConverter.ToSingle(packet, 29),
                    AccelX = BitConverter.ToSingle(packet, 33),
                    AccelY = BitConverter.ToSingle(packet, 37),
                    AccelZ = BitConverter.ToSingle(packet, 41),
                    Angle = BitConverter.ToSingle(packet, 45),
                    RocketTemperature = BitConverter.ToSingle(packet, 49),
                    RocketPressure = BitConverter.ToSingle(packet, 53),
                    RocketSpeed = BitConverter.ToSingle(packet, 57),
                    status = packet[61],
                    CRC = packet[62],
                    TeamID = 255, // Default team ID
                };

                // CRC validation
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, 58);
                if (calculatedCRC != packet[62])
                {
                    Debug.WriteLine($"Roket CRC hatasý! Hesaplanan: 0x{calculatedCRC:X2}, Gelen: 0x{packet[62]:X2}");
                }

                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Roket paketi parse hatasý: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Payload paketini parse eder
        /// </summary>
        private static PayloadTelemetryData? ParsePayloadData(byte[] packet)
        {
            try
            {
                if (!IsValidPacket(packet, PAYLOAD_HEADER))
                    return null;

                var data = new PayloadTelemetryData
                {
                    PacketCounter = packet[4],
                    PayloadAltitude = BitConverter.ToSingle(packet, 5),
                    PayloadGpsAltitude = BitConverter.ToSingle(packet, 9),
                    PayloadLatitude = BitConverter.ToSingle(packet, 13),
                    PayloadLongitude = BitConverter.ToSingle(packet, 17),
                    PayloadSpeed = BitConverter.ToSingle(packet, 21),
                    PayloadTemperature = BitConverter.ToSingle(packet, 25),
                    PayloadPressure = BitConverter.ToSingle(packet, 29),
                    PayloadHumidity = BitConverter.ToSingle(packet, 33),
                    CRC = packet[37],
                };

                // CRC validation
                byte calculatedCRC = CalculateSimpleCRC(packet, 4, 33);
                if (calculatedCRC != packet[37])
                {
                    Debug.WriteLine($"Payload CRC hatasý! Hesaplanan: 0x{calculatedCRC:X2}, Gelen: 0x{packet[37]:X2}");
                }

                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Payload paketi parse hatasý: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// HYI paketini parse eder
        /// </summary>
        private static HYITelemetryData? ParseHYIData(byte[] packet)
        {
            try
            {
                if (!IsValidPacket(packet, HYI_HEADER))
                    return null;

                var data = new HYITelemetryData
                {
                    TeamId = packet[4],
                    PacketCounter = packet[5],
                    Altitude = BitConverter.ToSingle(packet, 6),
                    RocketGpsAltitude = BitConverter.ToSingle(packet, 10),
                    RocketLatitude = BitConverter.ToSingle(packet, 14),
                    RocketLongitude = BitConverter.ToSingle(packet, 18),
                    PayloadGpsAltitude = BitConverter.ToSingle(packet, 22),
                    PayloadLatitude = BitConverter.ToSingle(packet, 26),
                    PayloadLongitude = BitConverter.ToSingle(packet, 30),
                    StageGpsAltitude = BitConverter.ToSingle(packet, 34),
                    StageLatitude = BitConverter.ToSingle(packet, 38),
                    StageLongitude = BitConverter.ToSingle(packet, 42),
                    GyroscopeX = BitConverter.ToSingle(packet, 46),
                    GyroscopeY = BitConverter.ToSingle(packet, 50),
                    GyroscopeZ = BitConverter.ToSingle(packet, 54),
                    AccelerationX = BitConverter.ToSingle(packet, 58),
                    AccelerationY = BitConverter.ToSingle(packet, 62),
                    AccelerationZ = BitConverter.ToSingle(packet, 66),
                    Angle = BitConverter.ToSingle(packet, 70),
                    Status = packet[74],
                    CRC = packet[75]
                };

                // CRC validation
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, 71);
                if (calculatedCRC != packet[75])
                {
                    Debug.WriteLine($"HYI CRC hatasý! Hesaplanan: 0x{calculatedCRC:X2}, Gelen: 0x{packet[75]:X2}");
                }

                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HYI paketi parse hatasý: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// HYIDenem paketini parse eder
        /// </summary>
        private static HYIDenemeData? ParseHYIDenemeData(byte[] packet)
        {
            try
            {
                if (!IsValidPacket(packet, HYIDENEM_HEADER))
                    return null;

                var data = new HYIDenemeData
                {
                    TeamId = packet[4],
                    PacketCounter = packet[5],
                    
                    // Roket telemetrileri
                    RocketAltitude = BitConverter.ToSingle(packet, 6),
                    RocketGpsAltitude = BitConverter.ToSingle(packet, 10),
                    RocketLatitude = BitConverter.ToSingle(packet, 14),
                    RocketLongitude = BitConverter.ToSingle(packet, 18),
                    GyroX = BitConverter.ToSingle(packet, 22),
                    GyroY = BitConverter.ToSingle(packet, 26),
                    GyroZ = BitConverter.ToSingle(packet, 30),
                    AccelX = BitConverter.ToSingle(packet, 34),
                    AccelY = BitConverter.ToSingle(packet, 38),
                    AccelZ = BitConverter.ToSingle(packet, 42),
                    Angle = BitConverter.ToSingle(packet, 46),
                    RocketSpeed = BitConverter.ToSingle(packet, 50),
                    RocketTemperature = BitConverter.ToSingle(packet, 54),
                    RocketPressure = BitConverter.ToSingle(packet, 58),
                    RocketStatus = packet[62],
                    
                    // Payload verileri
                    PayloadAltitude = BitConverter.ToSingle(packet, 63),
                    PayloadGpsAltitude = BitConverter.ToSingle(packet, 67),
                    PayloadLatitude = BitConverter.ToSingle(packet, 71),
                    PayloadLongitude = BitConverter.ToSingle(packet, 75),
                    PayloadSpeed = BitConverter.ToSingle(packet, 79),
                    PayloadTemperature = BitConverter.ToSingle(packet, 83),
                    PayloadPressure = BitConverter.ToSingle(packet, 87),
                    PayloadHumidity = BitConverter.ToSingle(packet, 91),
                    
                    CRC = packet[95]
                };

                return data;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HYIDenem paketi parse hatasý: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region Utility Methods
        /// <summary>
        /// Paket header'ýnýn geçerli olup olmadýðýný kontrol eder
        /// </summary>
        private static bool IsValidPacket(byte[] packet, byte[] expectedHeader)
        {
            if (packet.Length < expectedHeader.Length)
                return false;

            for (int i = 0; i < expectedHeader.Length; i++)
            {
                if (packet[i] != expectedHeader[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Buffer'da belirtilen header'ý arar
        /// </summary>
        private static int FindHeader(List<byte> buffer, byte[] header)
        {
            for (int i = 0; i <= buffer.Count - header.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < header.Length; j++)
                {
                    if (buffer[i + j] != header[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Checksum hesaplama (toplama yöntemi)
        /// </summary>
        private static byte CalculateChecksumAddition(byte[] data, int offset, int length)
        {
            int sum = 0;
            for (int i = offset; i < offset + length; i++)
            {
                sum += data[i];
            }
            return (byte)(sum % 256);
        }

        /// <summary>
        /// Basit CRC hesaplama (XOR yöntemi)
        /// </summary>
        private static byte CalculateSimpleCRC(byte[] data, int offset, int length)
        {
            byte crc = 0;
            for (int i = offset; i < offset + length; i++)
            {
                crc ^= data[i];
            }
            return crc;
        }
        #endregion
    }
}