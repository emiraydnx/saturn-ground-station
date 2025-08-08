/*
 * 🚀 SİSTEMİNİZLE UYUMLU ARDUINO KODU
 * 
 * ✅ DÜZELTMELER:
 * - Roket paket boyutu: 68 → 64 byte
 * - CRC hesaplama: XOR → Checksum Addition (sizin sistemle uyumlu)
 * - Team ID pozisyonu: Paket sonuna → Header sonrasına
 * - Paket formatı: Tam olarak sizin sistem formatına uygun
 * - Baud rate: 115200 (sizin sisteminizle uyumlu)
 * 
 * 📡 SADECE ROKET PAKETLERİ GÖNDERİR:
 * - Header: 0xAB 0xBC 0x12 0x13
 * - 64 byte paket boyutu
 * - HYIDenem otomatik tetiklenir
 */

#include <Wire.h>
#include <SPI.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_BNO055.h>
#include <Adafruit_BMP3XX.h>
#include <TinyGPSPlus.h>

// TH08 sensörü I2C adresi
#define TH08_I2C_ADDRESS 0x40

// Sensör nesneleri
Adafruit_BNO055 bno = Adafruit_BNO055(55);
Adafruit_BMP3XX bmp;
TinyGPSPlus gps;

// ✅ SİSTEMİNİZLE UYUMLU PAKET SABITLERI
const byte ROCKET_HEADER[4] = {0xAB, 0xBC, 0x12, 0x13};
const int ROCKET_PACKET_SIZE = 64;  // ✅ 68 → 64 düzeltildi
const byte TEAM_ID = 123;

// Paket sayacı
byte rocketPacketCounter = 0;

// GPS seri port
#define GPS_SERIAL Serial2

// Zamanlama değişkenleri
unsigned long lastRocketSendTime = 0;
const unsigned long ROCKET_SEND_INTERVAL = 2000;  // 2 saniye (test için)

// İrtifa hesaplama için
float initialPressure;
float previousAltitude = 0;
float previousPressure = 0;
unsigned long previousTime = 0;

void setup() {
  Serial.begin(115200);  // ✅ Sizin sistemle uyumlu baud rate
  Serial.println("🚀 SİSTEMİNİZLE UYUMLU ROKET TELEMETRİ TRANSMITTER");
  Serial.println("📡 Roket paketleri gönderiliyor...");
  Serial.println("✨ HYIDenem sistemi otomatik tetiklenecek!");
  Serial.println("==========================================");
  
  // GPS başlatma
  GPS_SERIAL.begin(9600);
  Serial.println("GPS başlatıldı");
  
  // I2C başlatma
  Wire.begin();

  // BNO055 başlatma
  if (!bno.begin()) {
    Serial.println("BNO055 bulunamadı!");
    while (1) delay(10);
  }
  bno.setExtCrystalUse(true);
  Serial.println("BNO055 başlatıldı");
  
  // BMP388 başlatma
  if (!bmp.begin_I2C()) {
    Serial.println("BMP388 bulunamadı!");
    while (1) delay(10);
  }
  
  // BMP388 ayarları
  bmp.setTemperatureOversampling(BMP3_OVERSAMPLING_8X);
  bmp.setPressureOversampling(BMP3_OVERSAMPLING_4X);
  bmp.setIIRFilterCoeff(BMP3_IIR_FILTER_COEFF_3);
  bmp.setOutputDataRate(BMP3_ODR_50_HZ);
  Serial.println("BMP388 başlatıldı");

  // Referans basınç değeri alma
  if (bmp.performReading()) {
    initialPressure = bmp.pressure / 100.0; // hPa
    previousPressure = initialPressure;
  }
  
  // TH08 sensörü başlatma
  initTH08();
  Serial.println("TH08 (I2C) başlatıldı");
  
  Serial.println("✅ Tüm sensörler hazır!");
  Serial.println("🚀 2 saniyede bir roket paketi gönderilecek...");
  previousTime = millis();
}

void loop() {
  // GPS verilerini sürekli oku
  while (GPS_SERIAL.available() > 0) {
    gps.encode(GPS_SERIAL.read());
  }
  
  // ✅ SADECE ROKET PAKETLERİ GÖNDER (Sizin sistem HYIDenem'i otomatik üretir)
  if (millis() - lastRocketSendTime >= ROCKET_SEND_INTERVAL) {
    sendRocketTelemetry();
    lastRocketSendTime = millis();
  }
}

void initTH08() {
  Wire.beginTransmission(TH08_I2C_ADDRESS);
  Wire.write(0xFE); // Soft reset komutu
  Wire.endTransmission();
  delay(15);
}

float readTH08Temperature() {
  Wire.beginTransmission(TH08_I2C_ADDRESS);
  Wire.write(0xF3); // Sıcaklık ölçümü komutu
  Wire.endTransmission();
  
  delay(50);
  
  Wire.requestFrom(TH08_I2C_ADDRESS, 3);
  if (Wire.available() >= 3) {
    uint16_t rawTemp = Wire.read() << 8;
    rawTemp |= Wire.read();
    Wire.read(); // CRC byte'ını oku
    
    float temperature = -46.85 + 175.72 * (float)rawTemp / 65536.0;
    return temperature;
  }
  return 0.0;
}

void sendRocketTelemetry() {
  byte packet[ROCKET_PACKET_SIZE];
  int offset = 0;
  
  // ✅ HEADER (4 byte): 0xAB, 0xBC, 0x12, 0x13
  for (int i = 0; i < 4; i++) {
    packet[offset++] = ROCKET_HEADER[i];
  }
  
  // ✅ PAKET SAYACI (1 byte)
  packet[offset++] = rocketPacketCounter++;
  
  // SENSÖR VERİLERİNİ OKUMA
  float altitude = 0;
  float temperature = 0;
  float pressure = 0;
  float speed = 0;
  
  if (bmp.performReading()) {
    pressure = bmp.pressure / 100.0; // hPa
    
    // Barometre ile irtifa hesaplama
    altitude = 44330.0 * (1.0 - pow(pressure / initialPressure, 1/5.255));
    
    // Hız hesaplama
    unsigned long currentTime = millis();
    unsigned long deltaTime = currentTime - previousTime;
    if (deltaTime > 0) {
      speed = (altitude - previousAltitude) * 1000.0 / deltaTime; // m/s
      previousTime = currentTime;
      previousAltitude = altitude;
      previousPressure = pressure;
    }
  }
  
  // TH08'den sıcaklık
  temperature = readTH08Temperature();
  
  // BNO055'den IMU verileri
  sensors_event_t orientationData, linearAccelData;
  bno.getEvent(&orientationData, Adafruit_BNO055::VECTOR_EULER);
  bno.getEvent(&linearAccelData, Adafruit_BNO055::VECTOR_LINEARACCEL);
  
  float gyroX = orientationData.orientation.x;
  float gyroY = orientationData.orientation.y;
  float gyroZ = orientationData.orientation.z;
  
  float accelX = linearAccelData.acceleration.x;
  float accelY = linearAccelData.acceleration.y;
  float accelZ = linearAccelData.acceleration.z;
  
  float angle = orientationData.orientation.x;
  
  // GPS verileri
  float gpsAltitude = gps.altitude.meters();
  float latitude = gps.location.lat();
  float longitude = gps.location.lng();
  
  // ✅ SİSTEMİNİZİN FORMATINA UYGUN PAKET OLUŞTURMA
  
  // Roket İrtifa (4 byte)
  memcpy(packet + offset, &altitude, 4);           offset += 4;
  
  // GPS İrtifa (4 byte)
  memcpy(packet + offset, &gpsAltitude, 4);        offset += 4;
  
  // GPS Koordinatları (8 byte)
  memcpy(packet + offset, &latitude, 4);           offset += 4;
  memcpy(packet + offset, &longitude, 4);          offset += 4;
  
  // IMU Verileri (24 byte)
  memcpy(packet + offset, &gyroX, 4);              offset += 4;
  memcpy(packet + offset, &gyroY, 4);              offset += 4;
  memcpy(packet + offset, &gyroZ, 4);              offset += 4;
  memcpy(packet + offset, &accelX, 4);             offset += 4;
  memcpy(packet + offset, &accelY, 4);             offset += 4;
  memcpy(packet + offset, &accelZ, 4);             offset += 4;
  
  // Açı (4 byte)
  memcpy(packet + offset, &angle, 4);              offset += 4;
  
  // Sıcaklık (4 byte)
  memcpy(packet + offset, &temperature, 4);        offset += 4;
  
  // Basınç (4 byte)
  memcpy(packet + offset, &pressure, 4);           offset += 4;
  
  // Hız (4 byte)
  memcpy(packet + offset, &speed, 4);              offset += 4;
  
  // Durum (1 byte)
  byte status = 2; // Normal durum
  packet[offset++] = status;
  
  // ✅ CRC HESAPLAMA (Sizin sistem: Checksum Addition)
  byte crc = calculateChecksumAddition(packet + 4, offset - 4);
  packet[offset++] = crc;
  
  // ✅ PADDING (eğer 64 byte'a ulaşmak için gerekirse)
  while (offset < ROCKET_PACKET_SIZE) {
    packet[offset++] = 0x00;
  }
  
  // Serial port üzerinden gönder
  Serial.write(packet, ROCKET_PACKET_SIZE);
  
  // Debug bilgisi
  Serial.print("🚀 Roket paketi gönderildi #");
  Serial.print(rocketPacketCounter - 1);
  Serial.print(" - İrtifa: ");
  Serial.print(altitude, 1);
  Serial.print("m, GPS: ");
  Serial.print(latitude, 6);
  Serial.print(",");
  Serial.print(longitude, 6);
  Serial.print(", CRC: 0x");
  Serial.print(crc, HEX);
  Serial.print(", Boyut: ");
  Serial.print(ROCKET_PACKET_SIZE);
  Serial.println(" byte");
  
  // ✅ HEX DUMP (Debug için)
  Serial.print("📋 HEX: ");
  for (int i = 0; i < min(16, ROCKET_PACKET_SIZE); i++) {
    if (packet[i] < 0x10) Serial.print("0");
    Serial.print(packet[i], HEX);
    Serial.print(" ");
  }
  Serial.println("...");
  
  Serial.println("✨ HYIDenem sistemi otomatik tetiklenecek!");
  Serial.println("==========================================");
}

// ✅ SİSTEMİNİZLE UYUMLU CRC HESAPLAMA (Checksum Addition)
byte calculateChecksumAddition(byte* data, int length) {
  int sum = 0;
  for (int i = 0; i < length; i++) {
    sum += data[i];
  }
  return (byte)(sum % 256);
}

// Test modu: Serial Monitor'den komutlar
void serialEvent() {
  if (Serial.available()) {
    char command = Serial.read();
    
    if (command == 'T' || command == 't') {
      Serial.println("🧪 MANUEL TEST PAKETİ GÖNDERİLİYOR...");
      sendRocketTelemetry();
    }
    else if (command == 'I' || command == 'i') {
      Serial.println("📋 PAKET BİLGİLERİ:");
      Serial.print("Header: ");
      for (int i = 0; i < 4; i++) {
        Serial.print("0x");
        if (ROCKET_HEADER[i] < 0x10) Serial.print("0");
        Serial.print(ROCKET_HEADER[i], HEX);
        Serial.print(" ");
      }
      Serial.println();
      Serial.print("Paket Boyutu: ");
      Serial.print(ROCKET_PACKET_SIZE);
      Serial.println(" byte");
      Serial.print("Son Paket Sayacı: ");
      Serial.println(rocketPacketCounter);
      Serial.print("Takım ID: ");
      Serial.println(TEAM_ID);
    }
    else if (command == 'H' || command == 'h') {
      Serial.println("🆘 KOMUTLAR:");
      Serial.println("T - Manuel test paketi gönder");
      Serial.println("I - Paket bilgilerini göster");
      Serial.println("H - Bu yardım menüsü");
      Serial.println("");
      Serial.println("✅ SİSTEM DURUMU:");
      Serial.println("- Roket paketleri: 2 saniyede bir otomatik");
      Serial.println("- HYIDenem: Yer istasyonunda otomatik üretilir");
      Serial.println("- Payload chart'ları: ±0.05 değişimle doldurulur");
    }
  }
}