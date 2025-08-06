/*
  Arduino Roket Telemetri Test Kodu
  Sadece Roket verilerini gönderir (Payload yok)
  
  Paket Formatý:
  Header: 0xAB, 0xBC, 0x12, 0x13 (4 byte)
  Packet Counter: 1 byte
  Rocket Altitude: 4 byte (float)
  Rocket GPS Altitude: 4 byte (float) 
  Rocket Latitude: 4 byte (float)
  Rocket Longitude: 4 byte (float)
  Gyro X: 4 byte (float)
  Gyro Y: 4 byte (float) 
  Gyro Z: 4 byte (float)
  Accel X: 4 byte (float)
  Accel Y: 4 byte (float)
  Accel Z: 4 byte (float)
  Angle: 4 byte (float)
  Temperature: 4 byte (float)
  Pressure: 4 byte (float)
  Speed: 4 byte (float)
  Status: 1 byte
  CRC: 1 byte
  Team ID: Sabit 255
  
  Toplam: 64 byte
*/

// Roket paket yapýsý
struct RocketTelemetryPacket {
  uint8_t header[4] = {0xAB, 0xBC, 0x12, 0x13};
  uint8_t packetCounter;
  float rocketAltitude;
  float rocketGpsAltitude;
  float rocketLatitude;
  float rocketLongitude;
  float gyroX;
  float gyroY;
  float gyroZ;
  float accelX;
  float accelY;
  float accelZ;
  float angle;
  float rocketTemperature;
  float rocketPressure;
  float rocketSpeed;
  uint8_t status;
  uint8_t crc;
  uint8_t teamID = 255;
};

// Global deðiþkenler
RocketTelemetryPacket rocketPacket;
uint8_t packetCounter = 0;
unsigned long lastSendTime = 0;
const unsigned long SEND_INTERVAL = 1000; // 1 saniye aralýk

// Test için simülasyon deðiþkenleri
float altitude = 0.0;
float speed = 0.0;
float temperature = 20.0;
float pressure = 1013.25;
bool ascending = true;

void setup() {
  Serial.begin(9600);
  Serial.println("?? Arduino Roket Telemetri Test");
  Serial.println("================================");
  Serial.println("Sadece roket verisi gönderiliyor...");
  Serial.println("Header: 0xAB 0xBC 0x12 0x13");
  Serial.println("Paket boyutu: 64 byte");
  Serial.println("Gönderim aralýðý: 1 saniye");
  Serial.println("");
  
  // Ýlk deðerleri ayarla
  initializeRocketData();
}

void loop() {
  unsigned long currentTime = millis();
  
  if (currentTime - lastSendTime >= SEND_INTERVAL) {
    // Test verilerini güncelle
    updateTestData();
    
    // Roket paketini oluþtur ve gönder
    createRocketPacket();
    sendRocketPacket();
    
    // Debug bilgisi yazdýr
    printDebugInfo();
    
    lastSendTime = currentTime;
    packetCounter = (packetCounter + 1) % 256;
  }
}

void initializeRocketData() {
  rocketPacket.packetCounter = 0;
  rocketPacket.rocketAltitude = 0.0;
  rocketPacket.rocketGpsAltitude = 0.0;
  rocketPacket.rocketLatitude = 39.925533; // Ankara koordinatlarý
  rocketPacket.rocketLongitude = 32.866287;
  rocketPacket.gyroX = 0.0;
  rocketPacket.gyroY = 0.0;
  rocketPacket.gyroZ = 0.0;
  rocketPacket.accelX = 0.0;
  rocketPacket.accelY = 0.0;
  rocketPacket.accelZ = 9.81; // Yerçekimi
  rocketPacket.angle = 0.0;
  rocketPacket.rocketTemperature = 20.0;
  rocketPacket.rocketPressure = 1013.25;
  rocketPacket.rocketSpeed = 0.0;
  rocketPacket.status = 1; // Aktif
}

void updateTestData() {
  // Simülasyon: Roket yükseliyor/alçalýyor
  if (ascending) {
    altitude += random(5, 15); // 5-15m artýþ
    speed += random(1, 5);     // Hýz artýþý
    if (altitude > 1000) {     // 1000m'de zirve
      ascending = false;
    }
  } else {
    altitude -= random(3, 8);  // 3-8m azalýþ
    speed -= random(1, 3);     // Hýz azalýþý
    if (altitude < 0) {
      altitude = 0;
      speed = 0;
      ascending = true;        // Yeniden baþla
    }
  }
  
  // Sýcaklýk: Ýrtifa ile azalýr
  temperature = 20.0 - (altitude * 0.0065); // Standard atmosphere
  
  // Basýnç: Ýrtifa ile azalýr
  pressure = 1013.25 * pow(1 - (0.0065 * altitude / 288.15), 5.255);
  
  // Rastgele jiroskop deðerleri (roket dönüþü)
  float gyroRange = 50.0;
  rocketPacket.gyroX = random(-gyroRange * 100, gyroRange * 100) / 100.0;
  rocketPacket.gyroY = random(-gyroRange * 100, gyroRange * 100) / 100.0;
  rocketPacket.gyroZ = random(-gyroRange * 100, gyroRange * 100) / 100.0;
  
  // Ývme deðerleri (G kuvveti simülasyonu)
  rocketPacket.accelX = random(-200, 200) / 100.0; // ±2G
  rocketPacket.accelY = random(-200, 200) / 100.0; // ±2G
  rocketPacket.accelZ = 9.81 + random(-300, 300) / 100.0; // Yerçekimi ±3G
  
  // Açý (pitch)
  rocketPacket.angle = random(-180, 180);
  
  // GPS koordinatlarý (küçük deðiþimler)
  rocketPacket.rocketLatitude += random(-10, 10) / 1000000.0; // ±0.00001 derece
  rocketPacket.rocketLongitude += random(-10, 10) / 1000000.0;
}

void createRocketPacket() {
  rocketPacket.packetCounter = packetCounter;
  rocketPacket.rocketAltitude = altitude;
  rocketPacket.rocketGpsAltitude = altitude + random(-5, 5); // GPS noise
  rocketPacket.rocketTemperature = temperature;
  rocketPacket.rocketPressure = pressure;
  rocketPacket.rocketSpeed = speed;
  rocketPacket.status = ascending ? 2 : 3; // 2=Yükseliyor, 3=Alçalýyor
  
  // CRC hesapla (basit XOR)
  rocketPacket.crc = calculateCRC();
}

uint8_t calculateCRC() {
  uint8_t crc = 0;
  uint8_t* data = (uint8_t*)&rocketPacket;
  
  // Header'dan sonra CRC'ye kadar tüm veriyi XOR'la
  for (int i = 4; i < 62; i++) { // 4. byte'tan 62. byte'a kadar
    crc ^= data[i];
  }
  
  return crc;
}

void sendRocketPacket() {
  // Binary veriyi gönder
  Serial.write((uint8_t*)&rocketPacket, sizeof(rocketPacket));
  Serial.flush(); // Buffer'ý temizle
}

void printDebugInfo() {
  Serial.print("?? Paket #");
  Serial.print(packetCounter);
  Serial.print(" | Ýrtifa: ");
  Serial.print(altitude, 1);
  Serial.print("m | Hýz: ");
  Serial.print(speed, 1);
  Serial.print("m/s | Sýcaklýk: ");
  Serial.print(temperature, 1);
  Serial.print("°C | Durum: ");
  Serial.print(ascending ? "YUKSELIYOR" : "ALCALIYOR");
  Serial.print(" | CRC: 0x");
  Serial.print(rocketPacket.crc, HEX);
  Serial.println();
  
  // Detaylý bilgi (her 10 pakette bir)
  if (packetCounter % 10 == 0) {
    Serial.println("--- Detaylý Veri ---");
    Serial.print("GPS: ");
    Serial.print(rocketPacket.rocketLatitude, 6);
    Serial.print(", ");
    Serial.println(rocketPacket.rocketLongitude, 6);
    Serial.print("Jiroskop X,Y,Z: ");
    Serial.print(rocketPacket.gyroX, 2);
    Serial.print(", ");
    Serial.print(rocketPacket.gyroY, 2);
    Serial.print(", ");
    Serial.println(rocketPacket.gyroZ, 2);
    Serial.print("Ývme X,Y,Z: ");
    Serial.print(rocketPacket.accelX, 2);
    Serial.print(", ");
    Serial.print(rocketPacket.accelY, 2);
    Serial.print(", ");
    Serial.println(rocketPacket.accelZ, 2);
    Serial.print("Açý: ");
    Serial.print(rocketPacket.angle, 1);
    Serial.print("° | Basýnç: ");
    Serial.print(rocketPacket.rocketPressure, 1);
    Serial.println(" hPa");
    Serial.println("-------------------");
  }
}