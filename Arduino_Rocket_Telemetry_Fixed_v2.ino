/*
 * ?? ROKET TELEMETRÝ SÝMÜLATÖRÜ - DÜZELTÝLMÝÞ VERSÝYON v2
 * 
 * Bu kod C# SerialPortService ile tam uyumlu çalýþacak þekilde düzeltilmiþtir.
 * 
 * ? DÜZELTÝLEN SORUNLAR:
 * - CRC hesaplama algoritmasý C# ile uyumlu hale getirildi
 * - Float deðerler için little-endian byte sýralamasý 
 * - Paket boyutu tam 64 byte olarak ayarlandý
 * - Debug hex dump eklendi
 * 
 * ?? BAÐLANTI:
 * - Arduino USB'den bilgisayara baðlayýn
 * - Baud Rate: 115200
 * - SettingPage -> Input Port'tan Arduino portunu seçin
 * 
 * ?? SONUÇ:
 * - Roket chart'larý gerçek deðerlerle güncellenir
 * - CRC hatalarý giderildi
 * - Float deðerler doðru parse edilir
 */

#include <stdint.h>

// ?? ROKET PAKETÝ YAPISINI C# ILE TAM UYUMLU YAP
#pragma pack(push, 1) // Byte alignment için
struct RocketTelemetryPacket {
  // Header (4 byte): 0xAB, 0xBC, 0x12, 0x13
  uint8_t header[4];
  
  // Packet Counter (1 byte)
  uint8_t packetCounter;
  
  // Telemetri verileri (float - 4 byte each) - LITTLE ENDIAN
  float rocketAltitude;      // Byte 5-8
  float rocketGpsAltitude;   // Byte 9-12
  float rocketLatitude;      // Byte 13-16
  float rocketLongitude;     // Byte 17-20
  float gyroX;               // Byte 21-24
  float gyroY;               // Byte 25-28
  float gyroZ;               // Byte 29-32
  float accelX;              // Byte 33-36
  float accelY;              // Byte 37-40
  float accelZ;              // Byte 41-44
  float angle;               // Byte 45-48
  float rocketTemperature;   // Byte 49-52
  float rocketPressure;      // Byte 53-56
  float rocketSpeed;         // Byte 57-60
  
  // Status (1 byte) - Byte 61
  uint8_t status;
  
  // CRC (1 byte) - Byte 62
  uint8_t crc;
  
  // Padding (1 byte) - Byte 63 (64 byte toplam için)
  uint8_t padding;
};
#pragma pack(pop)

// Global deðiþkenler
RocketTelemetryPacket packet;
uint8_t packetCounter = 0;
unsigned long lastPacketTime = 0;
const unsigned long PACKET_INTERVAL = 2000; // 2 saniye

// ? C# SerialPortService ile ayný CRC algoritmasý
uint8_t calculateChecksumAddition(uint8_t* data, int offset, int length) {
  int sum = 0;
  for (int i = offset; i < offset + length; i++) {
    sum += data[i];
  }
  return (uint8_t)(sum % 256);
}

// Debug için hex dump fonksiyonu
void printHexDump(uint8_t* data, int length, String title) {
  Serial.print("?? ");
  Serial.print(title);
  Serial.print(": ");
  for (int i = 0; i < length && i < 32; i++) {
    if (data[i] < 0x10) Serial.print("0");
    Serial.print(data[i], HEX);
    Serial.print(" ");
  }
  if (length > 32) Serial.print("...");
  Serial.println();
}

void setup() {
  // Serial iletiþimi baþlat
  Serial.begin(115200);
  
  // Baþlangýçta 2 saniye bekle
  delay(2000);
  
  Serial.println("?? ROKET TELEMETRÝ SÝMÜLATÖRÜ v2 BAÞLATILDI!");
  Serial.println("?? Paket formatý: 64 byte roket telemetri (C# Uyumlu)");
  Serial.println("?? Her 2 saniyede bir paket gönderilecek...");
  Serial.println("? CRC Algoritmasý: Checksum Addition (C# uyumlu)");
  Serial.println("==========================================");
  
  // Paket header'ýný ayarla
  packet.header[0] = 0xAB;
  packet.header[1] = 0xBC;
  packet.header[2] = 0x12;
  packet.header[3] = 0x13;
  
  // Paket boyutunu kontrol et
  Serial.print("?? Paket boyutu: ");
  Serial.print(sizeof(RocketTelemetryPacket));
  Serial.println(" byte");
  
  if (sizeof(RocketTelemetryPacket) != 64) {
    Serial.println("?? UYARI: Paket boyutu 64 byte deðil!");
  }
  
  // Ýlk paketi hazýrla
  prepareRocketPacket();
}

void loop() {
  unsigned long currentTime = millis();
  
  // 2 saniyede bir paket gönder
  if (currentTime - lastPacketTime >= PACKET_INTERVAL) {
    sendRocketTelemetryPacket();
    lastPacketTime = currentTime;
  }
  
  // Küçük gecikme
  delay(10);
}

void prepareRocketPacket() {
  // Paket sayacýný güncelle
  packet.packetCounter = packetCounter++;
  
  // ?? TEST AMAÇLI TEMÝZ ROKET VERÝLERÝ (debug için sabit)
  packet.rocketAltitude = 234.7f;        // Ýrtifa (m)
  packet.rocketGpsAltitude = 236.2f;     // GPS Ýrtifa (m) 
  packet.rocketLatitude = 39.925533f;    // Enlem (Ankara)
  packet.rocketLongitude = 32.866287f;   // Boylam (Ankara)
  
  // Jiroskop verileri (derece/saniye)
  packet.gyroX = 12.5f;
  packet.gyroY = -8.3f;
  packet.gyroZ = 15.7f;
  
  // Ývme verileri (m/s²)
  packet.accelX = 2.1f;
  packet.accelY = -1.2f;
  packet.accelZ = 10.8f;
  
  // Diðer veriler
  packet.angle = 78.4f;                  // Açý (derece)
  packet.rocketTemperature = 23.5f;      // Sýcaklýk (°C)
  packet.rocketPressure = 1013.2f;       // Basýnç (hPa)
  packet.rocketSpeed = 45.8f;            // Hýz (m/s)
  
  // Durum
  packet.status = 2;  // Roket durumu
  
  // ? CRC hesapla: C# SerialPortService ile ayný algoritma
  // Offset 4'ten baþlayarak 58 byte (status dahil, CRC hariç)
  packet.crc = calculateChecksumAddition((uint8_t*)&packet, 4, 58);
  
  // Padding
  packet.padding = 0x00;
}

void sendRocketTelemetryPacket() {
  // Paketi güncelle
  prepareRocketPacket();
  
  // ? DETAYLI DEBUG BÝLGÝSÝ
  Serial.println();
  Serial.println("?? ====== ROKET PAKETÝ GÖNDERÝLÝYOR ======");
  Serial.print("?? Paket #");
  Serial.print(packet.packetCounter);
  Serial.print(" - Boyut: ");
  Serial.print(sizeof(RocketTelemetryPacket));
  Serial.println(" byte");
  
  // Kritik deðerleri göster
  Serial.print("?? Ýrtifa: ");
  Serial.print(packet.rocketAltitude, 2);
  Serial.print("m, GPS Ýrtifa: ");
  Serial.print(packet.rocketGpsAltitude, 2);
  Serial.println("m");
  
  Serial.print("?? Koordinat: ");
  Serial.print(packet.rocketLatitude, 6);
  Serial.print(", ");
  Serial.println(packet.rocketLongitude, 6);
  
  Serial.print("?? Hýz: ");
  Serial.print(packet.rocketSpeed, 1);
  Serial.print("m/s, Sýcaklýk: ");
  Serial.print(packet.rocketTemperature, 1);
  Serial.println("°C");
  
  Serial.print("?? Status: ");
  Serial.print(packet.status);
  Serial.print(", CRC: 0x");
  Serial.print(packet.crc, HEX);
  Serial.print(" (");
  Serial.print(packet.crc);
  Serial.println(")");
  
  // CRC hesaplama detayý
  Serial.print("?? CRC Hesaplama: offset=4, length=58, sonuç=0x");
  Serial.println(packet.crc, HEX);
  
  // Hex dump - ilk 32 byte
  printHexDump((uint8_t*)&packet, sizeof(RocketTelemetryPacket), "TAM PAKET (Ýlk 32 byte)");
  
  // Binary paketi gönder
  Serial.write((uint8_t*)&packet, sizeof(RocketTelemetryPacket));
  
  Serial.println("? Binary paket gönderildi!");
  Serial.println("==========================================");
}

// Test modu: Manuel paket gönderimi
void serialEvent() {
  if (Serial.available()) {
    char command = Serial.read();
    
    if (command == 'T' || command == 't') {
      Serial.println("?? MANUEL TEST PAKETÝ GÖNDERÝLÝYOR...");
      sendRocketTelemetryPacket();
    }
    else if (command == 'I' || command == 'i') {
      // Paket bilgilerini yazdýr
      Serial.println("?? PAKET BÝLGÝLERÝ:");
      Serial.print("Header: 0x");
      for (int i = 0; i < 4; i++) {
        if (packet.header[i] < 0x10) Serial.print("0");
        Serial.print(packet.header[i], HEX);
        Serial.print(" ");
      }
      Serial.println();
      Serial.print("Paket Boyutu: ");
      Serial.print(sizeof(RocketTelemetryPacket));
      Serial.println(" byte");
      Serial.print("Son Paket Sayacý: ");
      Serial.println(packet.packetCounter);
      Serial.print("Ýrtifa: ");
      Serial.print(packet.rocketAltitude, 2);
      Serial.println("m");
    }
    else if (command == 'H' || command == 'h') {
      // Yardým
      Serial.println("?? KOMUTLAR:");
      Serial.println("T - Manuel test paketi gönder");
      Serial.println("I - Paket bilgilerini göster");
      Serial.println("H - Bu yardým menüsü");
    }
    else if (command == 'D' || command == 'd') {
      // Debug mode: farklý test deðerleri
      Serial.println("?? DEBUG MODU: Farklý deðerlerle test paketi");
      packet.rocketAltitude = 1234.56f;
      packet.rocketGpsAltitude = 1234.78f;
      packet.rocketLatitude = 40.123456f;
      packet.rocketLongitude = 29.123456f;
      sendRocketTelemetryPacket();
    }
  }
}