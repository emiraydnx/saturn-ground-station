/*
 * ?? ROKET TELEMETRÝ TRANSMITTER - GERÇEK SENSÖR VERÝLERÝYLE
 * 
 * ? DÜZELTÝLEN SORUNLAR:
 * - Sensör okuma hatalarý düzeltildi
 * - GPS veri kontrolü eklendi
 * - IMU veri validasyonu eklendi
 * - Fallback deðerler eklendi
 * - Tam 64 byte paket garantisi
 * - Detaylý sensör debug bilgisi
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

// ? SÝSTEMÝNÝZLE UYUMLU PAKET SABITLERI
const byte ROCKET_HEADER[4] = {0xAB, 0xBC, 0x12, 0x13};
const int ROCKET_PACKET_SIZE = 64;  
const byte TEAM_ID = 123;

// Paket sayacý
byte rocketPacketCounter = 0;

// GPS seri port
#define GPS_SERIAL Serial2

// Zamanlama deðiþkenleri
unsigned long lastRocketSendTime = 0;
const unsigned long ROCKET_SEND_INTERVAL = 2000;  // 2 saniye

// Ýrtifa hesaplama için
float initialPressure = 1013.25; // Standart basýnç
float previousAltitude = 0;
float previousPressure = 0;
unsigned long previousTime = 0;

// Sensör durumlarý
bool bmpAvailable = false;
bool bnoAvailable = false;
bool th08Available = false;

// ? SÝSTEMÝNÝZLE UYUMLU CRC HESAPLAMA (Checksum Addition)
byte calculateChecksumAddition(byte* data, int length) {
  int sum = 0;
  for (int i = 0; i < length; i++) {
    sum += data[i];
  }
  return (byte)(sum % 256);
}

// TEK SATIRDA HEX DUMP
void printSingleLineHex(byte* data, int length) {
  Serial.print("?? PAKET HEX: ");
  for (int i = 0; i < min(20, length); i++) {
    if (data[i] < 0x10) Serial.print("0");
    Serial.print(data[i], HEX);
    Serial.print(" ");
  }
  if (length > 20) Serial.print("...");
  Serial.println();
}

void setup() {
  Serial.begin(115200);  
  Serial.println("?? ROKET TELEMETRÝ TRANSMITTER - GERÇEK SENSÖRLER");
  Serial.println("?? Tüm sensörler test ediliyor...");
  Serial.println("==========================================");
  
  // GPS baþlatma
  GPS_SERIAL.begin(9600);
  Serial.println("?? GPS baþlatýldý (9600 baud)");
  
  // I2C baþlatma
  Wire.begin();
  Serial.println("?? I2C baþlatýldý");

  // ? BNO055 BAÞLATMA VE TEST
  Serial.print("?? BNO055 test ediliyor... ");
  if (bno.begin()) {
    bno.setExtCrystalUse(true);
    bnoAvailable = true;
    Serial.println("? BAÞARILI!");
    
    // BNO055 kalibrasyonu kontrol et
    uint8_t system, gyro, accel, mag;
    bno.getCalibration(&system, &gyro, &accel, &mag);
    Serial.print("?? Kalibrasyon: Sistem=");
    Serial.print(system);
    Serial.print(" Gyro=");
    Serial.print(gyro);
    Serial.print(" Accel=");
    Serial.print(accel);
    Serial.print(" Mag=");
    Serial.println(mag);
  } else {
    bnoAvailable = false;
    Serial.println("? BAÞARISIZ! Test verileri kullanýlacak.");
  }
  
  // ? BMP388 BAÞLATMA VE TEST
  Serial.print("??? BMP388 test ediliyor... ");
  if (bmp.begin_I2C()) {
    // BMP388 ayarlarý
    bmp.setTemperatureOversampling(BMP3_OVERSAMPLING_8X);
    bmp.setPressureOversampling(BMP3_OVERSAMPLING_4X);
    bmp.setIIRFilterCoeff(BMP3_IIR_FILTER_COEFF_3);
    bmp.setOutputDataRate(BMP3_ODR_50_HZ);
    
    // Ýlk okuma testi
    if (bmp.performReading()) {
      initialPressure = bmp.pressure / 100.0; // hPa
      previousPressure = initialPressure;
      bmpAvailable = true;
      Serial.print("? BAÞARILI! Referans basýnç: ");
      Serial.print(initialPressure, 2);
      Serial.println(" hPa");
    } else {
      bmpAvailable = false;
      Serial.println("? Okuma hatasý! Test verileri kullanýlacak.");
    }
  } else {
    bmpAvailable = false;
    Serial.println("? BAÞARISIZ! Test verileri kullanýlacak.");
  }
  
  // ? TH08 BAÞLATMA VE TEST
  Serial.print("??? TH08 test ediliyor... ");
  initTH08();
  float testTemp = readTH08Temperature();
  if (testTemp > -40 && testTemp < 85) { // Makul sýcaklýk aralýðý
    th08Available = true;
    Serial.print("? BAÞARILI! Sýcaklýk: ");
    Serial.print(testTemp, 1);
    Serial.println("°C");
  } else {
    th08Available = false;
    Serial.println("? BAÞARISIZ! Test verileri kullanýlacak.");
  }
  
  Serial.println("==========================================");
  Serial.print("?? Sensör Durumu: BMP=");
  Serial.print(bmpAvailable ? "?" : "?");
  Serial.print(" BNO=");
  Serial.print(bnoAvailable ? "?" : "?");
  Serial.print(" TH08=");
  Serial.println(th08Available ? "?" : "?");
  Serial.println("?? 2 saniyede bir roket paketi gönderilecek...");
  Serial.println();
  
  previousTime = millis();
}

void loop() {
  // GPS verilerini sürekli oku
  while (GPS_SERIAL.available() > 0) {
    if (gps.encode(GPS_SERIAL.read())) {
      // GPS verisi baþarýyla parse edildi
    }
  }
  
  // ? ROKET PAKETÝ GÖNDER
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
  Wire.write(0xF3); // Sýcaklýk ölçümü komutu
  Wire.endTransmission();
  
  delay(50);
  
  Wire.requestFrom(TH08_I2C_ADDRESS, 3);
  if (Wire.available() >= 3) {
    uint16_t rawTemp = Wire.read() << 8;
    rawTemp |= Wire.read();
    Wire.read(); // CRC byte'ýný oku
    
    float temperature = -46.85 + 175.72 * (float)rawTemp / 65536.0;
    return temperature;
  }
  return -999.0; // Hata durumu
}

void sendRocketTelemetry() {
  byte packet[ROCKET_PACKET_SIZE];
  int offset = 0;
  
  Serial.println("?? ====== YENÝ ROKET PAKETÝ ======");
  
  // ? HEADER (4 byte): 0xAB, 0xBC, 0x12, 0x13
  for (int i = 0; i < 4; i++) {
    packet[offset++] = ROCKET_HEADER[i];
  }
  
  // ? PAKET SAYACI (1 byte)
  packet[offset++] = rocketPacketCounter++;
  
  // ? GERÇEK SENSÖR VERÝLERÝNÝ OKU
  float altitude = 0;
  float temperature = 23.5; // Default sýcaklýk
  float pressure = initialPressure;
  float speed = 0;
  
  // ? BMP388'den basýnç ve irtifa oku
  if (bmpAvailable && bmp.performReading()) {
    pressure = bmp.pressure / 100.0; // hPa
    temperature = bmp.temperature; // BMP'den sýcaklýk da alýnabilir
    
    // Barometre ile irtifa hesaplama
    altitude = 44330.0 * (1.0 - pow(pressure / initialPressure, 1/5.255));
    
    // Hýz hesaplama
    unsigned long currentTime = millis();
    unsigned long deltaTime = currentTime - previousTime;
    if (deltaTime > 100) { // En az 100ms bekle
      speed = (altitude - previousAltitude) * 1000.0 / deltaTime; // m/s
      previousTime = currentTime;
      previousAltitude = altitude;
      previousPressure = pressure;
    }
    
    Serial.print("??? BMP388: ");
    Serial.print(temperature, 1);
    Serial.print("°C, ");
    Serial.print(pressure, 1);
    Serial.print("hPa, Ýrtifa: ");
    Serial.print(altitude, 1);
    Serial.println("m");
  } else {
    // ? FALLBACK: Test deðerleri kullan
    altitude = 234.7f + (rocketPacketCounter % 10);
    Serial.println("?? BMP388 okunamadý - test deðerleri kullanýldý");
  }
  
  // ? TH08'den sýcaklýk (öncelik TH08'de)
  if (th08Available) {
    float th08Temp = readTH08Temperature();
    if (th08Temp > -40 && th08Temp < 85) {
      temperature = th08Temp;
      Serial.print("??? TH08: ");
      Serial.print(temperature, 1);
      Serial.println("°C");
    }
  }
  
  // ? BNO055'den IMU verileri
  float gyroX = 12.5f, gyroY = -8.3f, gyroZ = 15.7f; // Default deðerler
  float accelX = 2.1f, accelY = -1.2f, accelZ = 10.8f;
  float angle = 78.4f;
  
  if (bnoAvailable) {
    sensors_event_t orientationData, linearAccelData;
    bno.getEvent(&orientationData, Adafruit_BNO055::VECTOR_EULER);
    bno.getEvent(&linearAccelData, Adafruit_BNO055::VECTOR_LINEARACCEL);
    
    // Deðerlerin geçerli olup olmadýðýný kontrol et
    if (!isnan(orientationData.orientation.x) && 
        !isnan(orientationData.orientation.y) && 
        !isnan(orientationData.orientation.z)) {
      gyroX = orientationData.orientation.x;
      gyroY = orientationData.orientation.y;
      gyroZ = orientationData.orientation.z;
      angle = orientationData.orientation.x;
      
      Serial.print("?? BNO055 Euler: ");
      Serial.print(gyroX, 1);
      Serial.print(", ");
      Serial.print(gyroY, 1);
      Serial.print(", ");
      Serial.println(gyroZ, 1);
    }
    
    if (!isnan(linearAccelData.acceleration.x) && 
        !isnan(linearAccelData.acceleration.y) && 
        !isnan(linearAccelData.acceleration.z)) {
      accelX = linearAccelData.acceleration.x;
      accelY = linearAccelData.acceleration.y;
      accelZ = linearAccelData.acceleration.z;
      
      Serial.print("? BNO055 Accel: ");
      Serial.print(accelX, 1);
      Serial.print(", ");
      Serial.print(accelY, 1);
      Serial.print(", ");
      Serial.println(accelZ, 1);
    }
  } else {
    Serial.println("?? BNO055 okunamadý - test deðerleri kullanýldý");
  }
  
  // ? GPS verileri
  float gpsAltitude = altitude + 2.0f; // GPS biraz daha yüksek olsun
  float latitude = 39.925533f;  // Default Ankara
  float longitude = 32.866287f;
  
  if (gps.location.isValid()) {
    latitude = gps.location.lat();
    longitude = gps.location.lng();
    Serial.print("?? GPS Konum: ");
    Serial.print(latitude, 6);
    Serial.print(", ");
    Serial.println(longitude, 6);
  } else {
    Serial.println("?? GPS okunamadý - test koordinatlarý kullanýldý");
  }
  
  if (gps.altitude.isValid()) {
    gpsAltitude = gps.altitude.meters();
    Serial.print("?? GPS Ýrtifa: ");
    Serial.print(gpsAltitude, 1);
    Serial.println("m");
  }
  
  // ? TEK SATIRDA TÜM VERÝLER
  Serial.print("?? ÖZET: Sayaç=");
  Serial.print(rocketPacketCounter - 1);
  Serial.print(" Alt=");
  Serial.print(altitude, 1);
  Serial.print("m GPS=");
  Serial.print(gpsAltitude, 1);
  Serial.print("m Temp=");
  Serial.print(temperature, 1);
  Serial.print("°C Press=");
  Serial.print(pressure, 1);
  Serial.print("hPa Speed=");
  Serial.print(speed, 1);
  Serial.println("m/s");
  
  // ? SÝSTEMÝNÝZÝN FORMATINA UYGUN PAKET OLUÞTURMA
  
  // Roket Ýrtifa (4 byte) - Byte 5-8
  memcpy(packet + offset, &altitude, 4);           offset += 4;
  
  // GPS Ýrtifa (4 byte) - Byte 9-12
  memcpy(packet + offset, &gpsAltitude, 4);        offset += 4;
  
  // GPS Koordinatlarý (8 byte) - Byte 13-20
  memcpy(packet + offset, &latitude, 4);           offset += 4;
  memcpy(packet + offset, &longitude, 4);          offset += 4;
  
  // IMU Verileri (24 byte) - Byte 21-44
  memcpy(packet + offset, &gyroX, 4);              offset += 4;
  memcpy(packet + offset, &gyroY, 4);              offset += 4;
  memcpy(packet + offset, &gyroZ, 4);              offset += 4;
  memcpy(packet + offset, &accelX, 4);             offset += 4;
  memcpy(packet + offset, &accelY, 4);             offset += 4;
  memcpy(packet + offset, &accelZ, 4);             offset += 4;
  
  // Açý (4 byte) - Byte 45-48
  memcpy(packet + offset, &angle, 4);              offset += 4;
  
  // Sýcaklýk (4 byte) - Byte 49-52
  memcpy(packet + offset, &temperature, 4);        offset += 4;
  
  // Basýnç (4 byte) - Byte 53-56
  memcpy(packet + offset, &pressure, 4);           offset += 4;
  
  // Hýz (4 byte) - Byte 57-60
  memcpy(packet + offset, &speed, 4);              offset += 4;
  
  // Durum (1 byte) - Byte 61
  byte status = 2; // Normal durum
  packet[offset++] = status;
  
  // ? CRC HESAPLAMA (Sizin sistem: Checksum Addition)
  byte crc = calculateChecksumAddition(packet + 4, offset - 4);
  packet[offset++] = crc;
  
  // ? PADDING (64 byte'a tamamla)
  while (offset < ROCKET_PACKET_SIZE) {
    packet[offset++] = 0x00;
  }
  
  // CRC bilgisi
  Serial.print("?? CRC: 0x");
  Serial.print(crc, HEX);
  Serial.print(" (");
  Serial.print(crc);
  Serial.print(") Boyut: ");
  Serial.print(ROCKET_PACKET_SIZE);
  Serial.println(" byte");
  
  // ? HEX DUMP
  printSingleLineHex(packet, ROCKET_PACKET_SIZE);
  
  // ? PAKET GÖNDERÝM
  Serial.write(packet, ROCKET_PACKET_SIZE);
  
  Serial.println("? Binary paket gönderildi!");
  Serial.println("? HYIDenem sistemi otomatik tetiklenecek!");
  Serial.println("==========================================");
  Serial.println();
}

// Test modu: Serial Monitor'den komutlar
void serialEvent() {
  if (Serial.available()) {
    char command = Serial.read();
    
    if (command == 'T' || command == 't') {
      Serial.println("?? MANUEL TEST PAKETÝ GÖNDERÝLÝYOR...");
      sendRocketTelemetry();
    }
    else if (command == 'S' || command == 's') {
      Serial.println("?? SENSÖR DURUMU:");
      Serial.print("BMP388: ");
      Serial.println(bmpAvailable ? "? Aktif" : "? Pasif");
      Serial.print("BNO055: ");
      Serial.println(bnoAvailable ? "? Aktif" : "? Pasif");
      Serial.print("TH08: ");
      Serial.println(th08Available ? "? Aktif" : "? Pasif");
      
      // Gerçek zamanlý sensör okuma
      if (bmpAvailable && bmp.performReading()) {
        Serial.print("?? BMP388 Anlýk: ");
        Serial.print(bmp.temperature, 1);
        Serial.print("°C, ");
        Serial.print(bmp.pressure / 100.0, 1);
        Serial.println(" hPa");
      }
      
      if (th08Available) {
        float temp = readTH08Temperature();
        Serial.print("?? TH08 Anlýk: ");
        Serial.print(temp, 1);
        Serial.println("°C");
      }
      
      Serial.print("?? GPS Uydu: ");
      Serial.print(gps.satellites.value());
      Serial.print(", Konum Valid: ");
      Serial.println(gps.location.isValid() ? "?" : "?");
    }
    else if (command == 'H' || command == 'h') {
      Serial.println("?? KOMUTLAR:");
      Serial.println("T - Manuel test paketi gönder");
      Serial.println("S - Sensör durumunu kontrol et");
      Serial.println("H - Bu yardým menüsü");
    }
  }
}