using System;

namespace copilot_deneme.TelemetryData
{
    /// <summary>
    /// Roket telemetri verilerini temsil eden sýnýf
    /// </summary>
    public class RocketTelemetryData
    {
        public float RocketAltitude { get; set; }
        public float RocketGpsAltitude { get; set; }
        public float RocketLatitude { get; set; }
        public float RocketLongitude { get; set; }
        public float GyroX { get; set; }
        public float GyroY { get; set; }
        public float GyroZ { get; set; }
        public float AccelX { get; set; }
        public float AccelY { get; set; }
        public float AccelZ { get; set; }
        public float Angle { get; set; }
        public float RocketSpeed { get; set; }
        public float RocketTemperature { get; set; }  
        public float RocketPressure { get; set; }
        public byte CRC { get; set; }
        public byte TeamID { get; set; }
        public byte status { get; set; }
        public byte PacketCounter { get; set; }
    }
        
    /// <summary>
    /// Payload telemetri verilerini temsil eden sýnýf
    /// </summary>
    public class PayloadTelemetryData
    {
        public float PayloadGpsAltitude { get; set; }
        public float PayloadAltitude { get; set; }
        public float PayloadLatitude { get; set; }
        public float PayloadLongitude { get; set; }
        public float PayloadSpeed { get; set; }
        public float PayloadTemperature { get; set; }
        public float PayloadPressure { get; set; }
        public float PayloadHumidity { get; set; }
        public byte CRC { get; set; }
        public byte PacketCounter { get; set; }
    }

    /// <summary>
    /// HYI telemetri verilerini temsil eden sýnýf
    /// </summary>
    public class HYITelemetryData
    {
        public byte TeamId { get; set; }
        public byte PacketCounter { get; set; }
        public float Altitude { get; set; }
        public float RocketGpsAltitude { get; set; }
        public float RocketLatitude { get; set; }
        public float RocketLongitude { get; set; }
        public float PayloadGpsAltitude { get; set; }
        public float PayloadLatitude { get; set; }
        public float PayloadLongitude { get; set; }
        public float StageGpsAltitude { get; set; }
        public float StageLatitude { get; set; }
        public float StageLongitude { get; set; }
        public float GyroscopeX { get; set; }
        public float GyroscopeY { get; set; }
        public float GyroscopeZ { get; set; }
        public float AccelerationX { get; set; }
        public float AccelerationY { get; set; }
        public float AccelerationZ { get; set; }
        public float Angle { get; set; }
        public byte Status { get; set; }
        public byte CRC { get; set; }
    }

    /// <summary>
    /// HYIDenem paketi - Roket telemetrilerinin hepsi + Payload verileri
    /// Payload GPS koordinatlarý roketle ayný, diðerleri sabit ±0.05 deðiþim
    /// </summary>
    public class HYIDenemeData
    {
        // Temel paket bilgileri
        public byte TeamId { get; set; }
        public byte PacketCounter { get; set; }
        
        // TÜM ROKET TELEMETRÝLERÝ
        public float RocketAltitude { get; set; }
        public float RocketGpsAltitude { get; set; }
        public float RocketLatitude { get; set; }
        public float RocketLongitude { get; set; }
        public float GyroX { get; set; }
        public float GyroY { get; set; }
        public float GyroZ { get; set; }
        public float AccelX { get; set; }
        public float AccelY { get; set; }
        public float AccelZ { get; set; }
        public float Angle { get; set; }
        public float RocketSpeed { get; set; }
        public float RocketTemperature { get; set; }  
        public float RocketPressure { get; set; }
        public byte RocketStatus { get; set; }
        
        // PAYLOAD VERÝLERÝ (GPS: roketle ayný, diðerleri: sabit ±0.05)
        public float PayloadGpsAltitude { get; set; }     // Roket GPS'inden türetilecek
        public float PayloadAltitude { get; set; }        // Sabit ±0.05
        public float PayloadLatitude { get; set; }        // Roket enlem ile ayný
        public float PayloadLongitude { get; set; }       // Roket boylam ile ayný
        public float PayloadSpeed { get; set; }           // Sabit ±0.05
        public float PayloadTemperature { get; set; }     // Sabit ±0.05
        public float PayloadPressure { get; set; }        // Sabit ±0.05
        public float PayloadHumidity { get; set; }        // Sabit ±0.05
        
        // Paket son bilgileri
        public byte CRC { get; set; }
    }

    /// <summary>
    /// Paket sabitleri ve konfigürasyonlarý
    /// </summary>
    public static class TelemetryConstants
    {
        // Paket boyutlarý
        public const int HYI_PACKET_SIZE = 78;
        public const int ROCKET_PACKET_SIZE = 64;
        public const int PAYLOAD_PACKET_SIZE = 34;
        public const int HYIDENEM_PACKET_SIZE = 98; // Roket (64) + Payload (34) = 98 byte

        // Header'lar
        public static readonly byte[] HYI_HEADER = { 0xFF, 0xFF, 0x54, 0x52 };
        public static readonly byte[] ROCKET_HEADER = { 0xAB, 0xBC, 0x12, 0x13 };
        public static readonly byte[] PAYLOAD_HEADER = { 0xCD, 0xDF, 0x14, 0x15 };
        public static readonly byte[] HYIDENEM_HEADER = { 0xDE, 0xAD, 0xBE, 0xEF }; // Özel header

        // Buffer sabitleri
        public const int MAX_BUFFER_SIZE = 4096; // Buffer overflow korumasý

        // HYIDenem test verileri için sabit deðerler (±0.05 deðiþim)
        public const float BASE_PAYLOAD_ALTITUDE = 320.5f;
        public const float BASE_PAYLOAD_SPEED = 45.8f;
        public const float BASE_PAYLOAD_TEMPERATURE = 22.3f;
        public const float BASE_PAYLOAD_PRESSURE = 1015.2f;
        public const float BASE_PAYLOAD_HUMIDITY = 65.7f;
        public const float PAYLOAD_VARIATION = 0.05f; // ±0.05 deðiþim
    }
}