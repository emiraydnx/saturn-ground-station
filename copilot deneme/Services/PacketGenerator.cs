using copilot_deneme.TelemetryData;
using copilot_deneme.Interfaces;
using System;
using System.Diagnostics;
using static copilot_deneme.TelemetryData.TelemetryConstants;

namespace copilot_deneme.Services
{
    /// <summary>
    /// Paket oluþturucu - Telemetri data sýnýflarýný binary paketlere dönüþtürür
    /// SerialPortService'den ayrýlan paket oluþturma sorumluluðu
    /// </summary>
    public class PacketGenerator : IPacketGenerator
    {
        #region HYI Packet Generation
        /// <summary>
        /// HYI telemetri verisini binary pakete dönüþtürür
        /// </summary>
        public byte[] CreateHYIPacket(HYITelemetryData data)
        {
            try
            {
                byte[] packet = new byte[HYI_PACKET_SIZE];
                int offset = 0;

                // Byte 1-4: Header (0xFF, 0xFF, 0x54, 0x52)
                packet[offset++] = 0xFF;
                packet[offset++] = 0xFF;
                packet[offset++] = 0x54;
                packet[offset++] = 0x52;

                // Byte 5: TAKIM ID
                packet[offset++] = data.TeamId;

                // Byte 6: PAKET SAYAÇ
                packet[offset++] = data.PacketCounter;

                // Float deðerleri sýrayla ekle
                BitConverter.GetBytes(data.Altitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketGpsAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketLatitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketLongitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadGpsAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadLatitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadLongitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.StageGpsAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.StageLatitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.StageLongitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroscopeX).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroscopeY).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroscopeZ).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.AccelerationX).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.AccelerationY).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.AccelerationZ).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.Angle).CopyTo(packet, offset); offset += 4;

                // Byte 75: DURUM
                packet[offset++] = data.Status;

                // Byte 76: CRC - Header hariç tüm data için
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, offset - 4);
                packet[offset++] = calculatedCRC;

                // Byte 77: 0x0D (CR)
                packet[offset++] = 0x0D;

                // Byte 78: 0x0A (LF)
                packet[offset++] = 0x0A;

                Debug.WriteLine($"HYI paketi oluþturuldu: {packet.Length} byte");
                return packet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HYI paket oluþturma hatasý: {ex.Message}");
                return new byte[HYI_PACKET_SIZE];
            }
        }

        /// <summary>
        /// Özel parametrelerle HYI paketi oluþturur
        /// </summary>
        public byte[] CreateCustomHYIPacket(
            byte teamId, byte packetCounter, float altitude,
            float rocketGpsAltitude, float rocketLatitude, float rocketLongitude,
            float payloadGpsAltitude, float payloadLatitude, float payloadLongitude,
            float stageGpsAltitude, float stageLatitude, float stageLongitude,
            float gyroscopeX, float gyroscopeY, float gyroscopeZ,
            float accelerationX, float accelerationY, float accelerationZ,
            float angle, byte status)
        {
            var hyiData = new HYITelemetryData
            {
                TeamId = teamId,
                PacketCounter = packetCounter,
                Altitude = altitude,
                RocketGpsAltitude = rocketGpsAltitude,
                RocketLatitude = rocketLatitude,
                RocketLongitude = rocketLongitude,
                PayloadGpsAltitude = payloadGpsAltitude,
                PayloadLatitude = payloadLatitude,
                PayloadLongitude = payloadLongitude,
                StageGpsAltitude = stageGpsAltitude,
                StageLatitude = stageLatitude,
                StageLongitude = stageLongitude,
                GyroscopeX = gyroscopeX,
                GyroscopeY = gyroscopeY,
                GyroscopeZ = gyroscopeZ,
                AccelerationX = accelerationX,
                AccelerationY = accelerationY,
                AccelerationZ = accelerationZ,
                Angle = angle,
                Status = status,
                CRC = 0 // CRC otomatik hesaplanacak
            };

            return CreateHYIPacket(hyiData);
        }

        /// <summary>
        /// Sýfýr deðerlerle HYI test paketi oluþturur
        /// </summary>
        public byte[] CreateZeroHYIPacket()
        {
            return CreateCustomHYIPacket(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        #endregion

        #region Test Packet Generation
        /// <summary>
        /// Test amaçlý roket paketi oluþturur
        /// </summary>
        public byte[] CreateTestRocketPacket(RocketTelemetryData data)
        {
            try
            {
                byte[] packet = new byte[ROCKET_PACKET_SIZE];
                int offset = 0;

                // Header (4 byte)
                Array.Copy(ROCKET_HEADER, 0, packet, offset, ROCKET_HEADER.Length);
                offset += ROCKET_HEADER.Length;

                // Packet Counter (1 byte)
                packet[offset++] = data.PacketCounter;

                // Float deðerleri sýrayla ekle
                BitConverter.GetBytes(data.RocketAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketGpsAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketLatitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketLongitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroX).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroY).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroZ).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.AccelX).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.AccelY).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.AccelZ).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.Angle).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketTemperature).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketPressure).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketSpeed).CopyTo(packet, offset); offset += 4;

                // Status (1 byte)
                packet[offset++] = data.status;

                // CRC (1 byte)
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, 58);
                packet[offset++] = calculatedCRC;

                // Padding byte
                if (offset < ROCKET_PACKET_SIZE)
                {
                    packet[offset] = 0x00;
                }

                return packet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Roket paket oluþturma hatasý: {ex.Message}");
                return new byte[ROCKET_PACKET_SIZE];
            }
        }

        /// <summary>
        /// Test amaçlý payload paketi oluþturur
        /// </summary>
        public byte[] CreateTestPayloadPacket(PayloadTelemetryData data)
        {
            try
            {
                byte[] packet = new byte[PAYLOAD_PACKET_SIZE];
                int offset = 0;

                // Header (4 byte)
                Array.Copy(PAYLOAD_HEADER, 0, packet, offset, PAYLOAD_HEADER.Length);
                offset += PAYLOAD_HEADER.Length;

                // Packet Counter (1 byte)
                packet[offset++] = data.PacketCounter;

                // Float deðerleri sýrayla ekle
                BitConverter.GetBytes(data.PayloadAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadGpsAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadLatitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadLongitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadSpeed).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadTemperature).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadPressure).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadHumidity).CopyTo(packet, offset); offset += 4;

                // CRC (1 byte)
                byte calculatedCRC = CalculateSimpleCRC(packet, 4, offset - 4);
                packet[offset] = calculatedCRC;

                return packet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Payload paket oluþturma hatasý: {ex.Message}");
                return new byte[PAYLOAD_PACKET_SIZE];
            }
        }

        /// <summary>
        /// HYIDenem paketi oluþturur
        /// </summary>
        public byte[] CreateHYIDenemePacket(HYIDenemeData data)
        {
            try
            {
                byte[] packet = new byte[HYIDENEM_PACKET_SIZE];
                int offset = 0;

                // Header (4 byte)
                Array.Copy(HYIDENEM_HEADER, 0, packet, offset, HYIDENEM_HEADER.Length);
                offset += HYIDENEM_HEADER.Length;

                // Temel bilgiler
                packet[offset++] = data.TeamId;
                packet[offset++] = data.PacketCounter;

                // Roket telemetrileri
                BitConverter.GetBytes(data.RocketAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketGpsAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketLatitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketLongitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroX).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroY).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.GyroZ).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.AccelX).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.AccelY).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.AccelZ).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.Angle).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketSpeed).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketTemperature).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.RocketPressure).CopyTo(packet, offset); offset += 4;
                packet[offset++] = data.RocketStatus;

                // Payload verileri
                BitConverter.GetBytes(data.PayloadAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadGpsAltitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadLatitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadLongitude).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadSpeed).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadTemperature).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadPressure).CopyTo(packet, offset); offset += 4;
                BitConverter.GetBytes(data.PayloadHumidity).CopyTo(packet, offset); offset += 4;

                // CRC
                byte calculatedCRC = CalculateChecksumAddition(packet, 4, offset - 4);
                packet[offset++] = calculatedCRC;

                // CR LF
                packet[offset++] = 0x0D;
                packet[offset++] = 0x0A;

                Debug.WriteLine($"HYIDenem paketi oluþturuldu: {packet.Length} byte");
                return packet;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HYIDenem paket oluþturma hatasý: {ex.Message}");
                return new byte[HYIDENEM_PACKET_SIZE];
            }
        }
        #endregion

        #region Utility Methods
        /// <summary>
        /// CRC hesaplama (toplama yöntemi)
        /// </summary>
        public byte CalculateChecksumAddition(byte[] data, int offset, int length)
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
        public byte CalculateSimpleCRC(byte[] data, int offset, int length)
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