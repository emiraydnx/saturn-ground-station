/*
 * ?? ROKET TELEMETRÝ SÝMÜLATÖRÜ - ARDUÝNO TEST KODU
 * 
 * Bu kod sisteminizin çalýþýp çalýþmadýðýný test etmek için tasarlanmýþtýr.
 * 
 * ? ÖZELLÝKLER:
 * - Roket telemetri paket formatýnda (64 byte) veri gönderir
 * - Header: 0xAB 0xBC 0x12 0x13
 * - Sabit deðerler gönderir (deðiþkenlik yok)
 * - Her 2 saniyede bir paket gönderir
 * - HYIDenem sistemini otomatik tetikler
 * 
 * ?? BAÐLANTI:
 * - Arduino USB'den bilgisayara baðlayýn
 * - Baud Rate: 115200
 * - SettingPage -> Input Port'tan Arduino portunu seçin
 * 
 * ?? SONUÇ:
 * - Roket chart'larý gerçek deðerlerle güncellenir
 * - Payload chart'larý ±0.05 deðiþimle otomatik oluþturulur
 * - HYIDenem paketi otomatik üretilir
 */

// Paket yapýsý
struct RocketTelemetryPacket {
  // Header (4 byte): 0xAB, 0xBC, 0x12, 0x13
  uint8_t header[4];
  
  // Packet Counter (1 byte)
  uint8_t packetCounter;
  
  // Telemetri verileri (float - 4 byte each)
  float rocketAltitude;      // 4 byte
  float rocketGpsAltitude;   // 4 byte
  float rocketLatitude;      // 4 byte
  float rocketLongitude;     // 4 byte
  float gyroX;               // 4 byte
  float gyroY;               // 4 byte
  float gyroZ;               // 4 byte
  float accelX;              // 4 byte
  float accelY;              // 4 byte
  float accelZ;              // 4 byte
  float angle;               // 4 byte
  float rocketTemperature;   // 4 byte
  float rocketPressure;      // 4 byte
  float rocketSpeed;         // 4 byte
  
  // Status (1 byte)
  uint8_t status;
  
  // CRC (1 byte)
  uint8_t crc;
  
  // Padding (eðer gerekirse)
  uint8_t padding;
};

// Global deðiþkenler
RocketTelemetryPacket packet;
uint8_t packetCounter = 0;
unsigned long lastPacketTime = 0;
const unsigned long PACKET_INTERVAL = 2000; // 2 saniye

// CRC hesaplama fonksiyonu (Checksum Addition)
uint8_t calculateChecksumAddition(uint8_t* data, int offset, int length) {
  int sum = 0;
  for (int i = offset; i < offset + length; i++) {
    sum += data[i];
  }
  return (uint8_t)(sum % 256);
}

void setup() {
  // Serial iletiþimi baþlat
  Serial.begin(115200);
  
  // Baþlangýçta 2 saniye bekle
  delay(2000);
  
  Serial.println("?? ROKET TELEMETRÝ SÝMÜLATÖRÜ BAÞLATILDI!");
  Serial.println("?? Paket formatý: 64 byte roket telemetri");
  Serial.println("?? Her 2 saniyede bir paket gönderilecek...");
  Serial.println("? HYIDenem sistemi otomatik tetiklenecek!");
  Serial.println("==========================================");
  
  // Paket header'ýný ayarla
  packet.header[0] = 0xAB;
  packet.header[1] = 0xBC;
  packet.header[2] = 0x12;
  packet.header[3] = 0x13;
  
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
  
  // ?? SABÝT ROKET TELEMETRÝ VERÝLERÝ
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
  
  // CRC hesapla (header hariç, status dahil)
  packet.crc = calculateChecksumAddition((uint8_t*)&packet, 4, 58);
  
  // Padding
  packet.padding = 0x00;
}

void sendRocketTelemetryPacket() {
  // Paketi güncelle
  prepareRocketPacket();
  
  // Binary paketi gönder
  Serial.write((uint8_t*)&packet, sizeof(RocketTelemetryPacket));
  
  // Debug bilgisi
  Serial.print("?? Roket paketi gönderildi #");
  Serial.print(packet.packetCounter);
  Serial.print(" - Ýrtifa: ");
  Serial.print(packet.rocketAltitude, 1);
  Serial.print("m, Hýz: ");
  Serial.print(packet.rocketSpeed, 1);
  Serial.print("m/s, GPS: ");
  Serial.print(packet.rocketLatitude, 6);
  Serial.print(",");
  Serial.print(packet.rocketLongitude, 6);
  Serial.print(", CRC: 0x");
  Serial.print(packet.crc, HEX);
  Serial.print(", Boyut: ");
  Serial.print(sizeof(RocketTelemetryPacket));
  Serial.println(" byte");
  
  // Hex dump (debug için)
  Serial.print("?? HEX: ");
  uint8_t* packetBytes = (uint8_t*)&packet;
  for (int i = 0; i < min(16, (int)sizeof(RocketTelemetryPacket)); i++) {
    if (packetBytes[i] < 0x10) Serial.print("0");
    Serial.print(packetBytes[i], HEX);
    Serial.print(" ");
  }
  Serial.println("...");
  
  Serial.println("? HYIDenem sistemi otomatik tetiklenecek!");
  Serial.println("==========================================");
}

// Test modu: Manuel paket gönderimi (Serial Monitor'den 'T' yazýn)
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
      Serial.print("Koordinatlar: ");
      Serial.print(packet.rocketLatitude, 6);
      Serial.print(", ");
      Serial.println(packet.rocketLongitude, 6);
    }
    else if (command == 'H' || command == 'h') {
      // Yardým
      Serial.println("?? KOMUTLAR:");
      Serial.println("T - Manuel test paketi gönder");
      Serial.println("I - Paket bilgilerini göster");
      Serial.println("H - Bu yardým menüsü");
    }
  }
}