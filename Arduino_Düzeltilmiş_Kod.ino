#include <Wire.h>
#include <SPI.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_BNO055.h>
#include <Adafruit_BMP3XX.h>
#include <TinyGPSPlus.h>
#include <LoRa.h>

// LoRa pin tanýmlamalarý (Teensy 4.0 için)
#define LORA_SS 10
#define LORA_RST 8
#define LORA_DIO0 4

// TH08 sensörü I2C adresi
#define TH08_I2C_ADDRESS 0x40

// Sensör nesneleri
Adafruit_BNO055 bno = Adafruit_BNO055(55);
Adafruit_BMP3XX bmp;
TinyGPSPlus gps;

// Telemetri paketleri için header tanýmlarý
const byte ROCKET_HEADER[4] = {0xAB, 0xBC, 0x12, 0x13};
const byte PAYLOAD_HEADER[4] = {0xCD, 0xDF, 0x14, 0x15};
const byte HYI_HEADER[4] = {0xFF, 0xFF, 0x54, 0x52};

// Paket boyutlarý - C# SerialPortService ile uyumlu
const int ROCKET_PACKET_SIZE = 64;  // Header(4) + PacketCounter(1) + 14*float(56) + status(1) + CRC(1) + TeamID(1) = 64 byte
const int PAYLOAD_PACKET_SIZE = 34; // Header(4) + PacketCounter(1) + 7*float(28) + CRC(1) = 34 byte
const int HYI_PACKET_SIZE = 78;     // Header(4) + TeamID(1) + PacketCounter(1) + 17*float(68) + status(1) + CRC(1) + padding(2) = 78 byte

// Paket sayaçlarý
byte rocketPacketCounter = 0;
byte payloadPacketCounter = 0;
byte hyiPacketCounter = 0;

// Takým ID
const byte TEAM_ID = 123;

// GPS seri port
#define GPS_SERIAL Serial2

// Zamanlama deðiþkenleri
unsigned long lastRocketSendTime = 0;
unsigned long lastPayloadSendTime = 0;
unsigned long lastHYISendTime = 0;
const unsigned long ROCKET_SEND_INTERVAL = 200;    // 200ms (5Hz)
const unsigned long PAYLOAD_SEND_INTERVAL = 500;   // 500ms (2Hz)
const unsigned long HYI_SEND_INTERVAL = 1000;      // 1000ms (1Hz)

// Her paket türü için ayrý irtifa ve zaman takibi
float rocketPreviousAltitude = 0;
unsigned long rocketPreviousTime = 0;
float payloadPreviousAltitude = 0;
unsigned long payloadPreviousTime = 0;

// Ýrtifa hesaplama için (ortak)
float initialPressure;      // Referans basýnç

void setup() {
  Serial.begin(115200);
  Serial.println("LoRa Transmitter - Roket Telemetri Sistemi Baþlatýlýyor...");
  
  // GPS baþlatma
  GPS_SERIAL.begin(9600);
  Serial.println("GPS baþlatýldý");
  
  // I2C baþlatma
  Wire.begin();

  // BNO055 baþlatma
  if (!bno.begin()) {
    Serial.println("BNO055 bulunamadý!");
    while (1) delay(10);
  }
  bno.setExtCrystalUse(true);
  Serial.println("BNO055 baþlatýldý");
  
  // BMP388 baþlatma
  if (!bmp.begin_I2C()) {
    Serial.println("BMP388 bulunamadý!");
    while (1) delay(10);
  }
  // BMP388 ayarlarý
  bmp.setTemperatureOversampling(BMP3_OVERSAMPLING_8X);
  bmp.setPressureOversampling(BMP3_OVERSAMPLING_4X);
  bmp.setIIRFilterCoeff(BMP3_IIR_FILTER_COEFF_3);
  bmp.setOutputDataRate(BMP3_ODR_50_HZ);
  Serial.println("BMP388 baþlatýldý");

  // Referans basýnç deðeri alma
  if (bmp.performReading()) {
    initialPressure = bmp.pressure / 100.0; // hPa
    Serial.print("Referans basýnç: ");
    Serial.print(initialPressure);
    Serial.println(" hPa");
  }
  
  // TH08 sensörü baþlatma (I2C ile)
  initTH08();
  Serial.println("TH08 (I2C) baþlatýldý");
  
  // LoRa baþlatma
  LoRa.setPins(LORA_SS, LORA_RST, LORA_DIO0);
  
  if (!LoRa.begin(868E6)) { // 868MHz
    Serial.println("LoRa baþlatýlamadý!");
    while (1) delay(10);
  }
  
  // LoRa ayarlarý
  LoRa.setTxPower(20);        // Transmit gücü (20dBm)
  LoRa.setSpreadingFactor(7); // Spreading Factor
  LoRa.setSignalBandwidth(125E3); // Bandwidth 125kHz
  LoRa.setCodingRate4(5);     // Coding Rate 4/5
  
  Serial.println("LoRa Transmitter 868MHz'de baþlatýldý");
  Serial.println("Tüm sensörler ve LoRa transmitter hazýr!");
  
  // Ýlk zaman deðerleri
  rocketPreviousTime = millis();
  payloadPreviousTime = millis();
}

void loop() {
  // GPS verilerini sürekli oku
  while (GPS_SERIAL.available() > 0) {
    gps.encode(GPS_SERIAL.read());
  }
  
  // Rocket Telemetry Paketi Gönderimi
  if (millis() - lastRocketSendTime >= ROCKET_SEND_INTERVAL) {
    sendRocketTelemetry();
    lastRocketSendTime = millis();
  }
  
  // Payload Telemetry Paketi Gönderimi
  if (millis() - lastPayloadSendTime >= PAYLOAD_SEND_INTERVAL) {
    sendPayloadTelemetry();
    lastPayloadSendTime = millis();
  }
  
  // HYI Paketi Gönderimi
  if (millis() - lastHYISendTime >= HYI_SEND_INTERVAL) {
    sendHYITelemetry();
    lastHYISendTime = millis();
  }
}

void initTH08() {
  // TH08 sensörü için gerekli baþlatma iþlemleri
  Wire.beginTransmission(TH08_I2C_ADDRESS);
  Wire.write(0xFE); // Soft reset komutu
  Wire.endTransmission();
  delay(15); // Reset için bekleme
}

float readTH08Temperature() {
  Wire.beginTransmission(TH08_I2C_ADDRESS);
  Wire.write(0xF3); // Sýcaklýk ölçümü komutu (no hold master)
  Wire.endTransmission();
  
  delay(50); // Ölçüm için bekleme
  
  Wire.requestFrom(TH08_I2C_ADDRESS, 3);
  if (Wire.available() >= 3) {
    uint16_t rawTemp = Wire.read() << 8;
    rawTemp |= Wire.read();
    // CRC byte'ýný oku ama kullanma
    Wire.read();
    
    // Sýcaklýk hesaplama (-46.85 + 175.72 * rawTemp / 65536)
    float temperature = -46.85 + 175.72 * (float)rawTemp / 65536.0;
    
    // Debug için sýcaklýk deðerini yazdýr
    Serial.print("TH08 Sýcaklýk: ");
    Serial.print(temperature);
    Serial.println(" °C");
    
    return temperature;
  }
  
  Serial.println("TH08 sýcaklýk okuma hatasý!");
  return 25.0; // Hata durumunda varsayýlan deðer
}

float readTH08Humidity() {
  Wire.beginTransmission(TH08_I2C_ADDRESS);
  Wire.write(0xF5); // Nem ölçümü komutu (no hold master)
  Wire.endTransmission();
  
  delay(50); // Ölçüm için bekleme
  
  Wire.requestFrom(TH08_I2C_ADDRESS, 3);
  if (Wire.available() >= 3) {
    uint16_t rawHum = Wire.read() << 8;
    rawHum |= Wire.read();
    // CRC byte'ýný oku ama kullanma
    Wire.read();
    
    // Nem hesaplama (-6 + 125 * rawHum / 65536)
    float humidity = -6.0 + 125.0 * (float)rawHum / 65536.0;
    
    // Nem deðerini 0-100 arasýnda sýnýrla
    if (humidity < 0) humidity = 0;
    if (humidity > 100) humidity = 100;
    
    return humidity;
  }
  return 50.0; // Hata durumunda varsayýlan deðer
}

void sendRocketTelemetry() {
  byte packet[ROCKET_PACKET_SIZE];
  int offset = 0;
  
  // Header (4 byte)
  for (int i = 0; i < 4; i++) {
    packet[offset++] = ROCKET_HEADER[i];
  }
  
  // Packet counter (1 byte)
  packet[offset++] = rocketPacketCounter++;
  
  // Sensör verilerini oku
  float altitude = 0;
  float temperature = 0;
  float pressure = 0;
  float speed = 0;
  
  // BMP388'den irtifa deðeri ve basýnç
  if (bmp.performReading()) {
    pressure = bmp.pressure / 100.0; // hPa
    
    // DÜZELTME: Doðru irtifa hesaplama formülü
    altitude = 44330.0 * (1.0 - pow(pressure / initialPressure, 0.1903));
    
    // Roket için hýz hesaplama
    unsigned long currentTime = millis();
    unsigned long deltaTime = currentTime - rocketPreviousTime;
    if (deltaTime > 0 && rocketPreviousTime > 0) {
      speed = (altitude - rocketPreviousAltitude) * 1000.0 / deltaTime; // m/s
    } else {
      speed = 0; // Ýlk ölçümde hýz sýfýr
    }
    
    // Roket için deðerleri güncelle
    rocketPreviousTime = currentTime;
    rocketPreviousAltitude = altitude;
  }
  
  // TH08'den sýcaklýk - DÜZELTME: BMP388 sýcaklýðýný da kontrol et
  temperature = readTH08Temperature();
  if (temperature == 25.0) { // TH08 hatasý varsa BMP388 kullan
    if (bmp.performReading()) {
      temperature = bmp.temperature;
      Serial.print("BMP388 Sýcaklýk kullanýlýyor: ");
      Serial.print(temperature);
      Serial.println(" °C");
    }
  }
  
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
  
  // GPS verisi
  float gpsAltitude = gps.altitude.meters();
  float latitude = gps.location.lat();
  float longitude = gps.location.lng();
  
  // DÜZELTME: C# ParseRocketData ile TAM UYUMLU sýralama
  memcpy(packet + offset, &altitude, 4);           offset += 4; // RocketAltitude (offset 5-8)
  memcpy(packet + offset, &gpsAltitude, 4);        offset += 4; // RocketGpsAltitude (offset 9-12)
  memcpy(packet + offset, &latitude, 4);           offset += 4; // RocketLatitude (offset 13-16)
  memcpy(packet + offset, &longitude, 4);          offset += 4; // RocketLongitude (offset 17-20)
  memcpy(packet + offset, &gyroX, 4);              offset += 4; // GyroX (offset 21-24)
  memcpy(packet + offset, &gyroY, 4);              offset += 4; // GyroY (offset 25-28)
  memcpy(packet + offset, &gyroZ, 4);              offset += 4; // GyroZ (offset 29-32)
  memcpy(packet + offset, &accelX, 4);             offset += 4; // AccelX (offset 33-36)
  memcpy(packet + offset, &accelY, 4);             offset += 4; // AccelY (offset 37-40)
  memcpy(packet + offset, &accelZ, 4);             offset += 4; // AccelZ (offset 41-44)
  memcpy(packet + offset, &angle, 4);              offset += 4; // Angle (offset 45-48)
  memcpy(packet + offset, &temperature, 4);        offset += 4; // RocketTemperature (offset 49-52) ?
  memcpy(packet + offset, &pressure, 4);           offset += 4; // RocketPressure (offset 53-56)
  memcpy(packet + offset, &speed, 4);              offset += 4; // RocketSpeed (offset 57-60)
  
  // Status (1 byte) - offset 61
  byte status = 1; // Normal durum
  packet[offset++] = status;
  
  // CRC hesaplama (Header hariç, status dahil) - offset 62
  byte crc = calculateCRC(packet + 4, offset - 4);
  packet[offset++] = crc;
  
  // Team ID - offset 63
  packet[offset++] = TEAM_ID;
  
  // LoRa ile gönder
  LoRa.beginPacket();
  LoRa.write(packet, ROCKET_PACKET_SIZE);
  LoRa.endPacket();
  
  // ÖNEMLI: Binary paketi Serial'e de gönder (PC için)
  Serial.write(packet, ROCKET_PACKET_SIZE);
  
  // Debug mesajý - sýcaklýk ve irtifa bilgisi ile
  Serial.print("Roket paketi #");
  Serial.print(rocketPacketCounter - 1);
  Serial.print(" - Ýrtifa: ");
  Serial.print(altitude);
  Serial.print("m, Sýcaklýk: ");
  Serial.print(temperature);
  Serial.println("°C");
}

void sendPayloadTelemetry() {
  byte packet[PAYLOAD_PACKET_SIZE];
  int offset = 0;
  
  // Header (4 byte)
  for (int i = 0; i < 4; i++) {
    packet[offset++] = PAYLOAD_HEADER[i];
  }
  
  // Packet counter (1 byte)
  packet[offset++] = payloadPacketCounter++;
  
  // Payload için sensör verileri
  float altitude = 0;
  float temperature = 0;
  float pressure = 0;
  float speed = 0;
  
  if (bmp.performReading()) {
    pressure = bmp.pressure / 100.0; // hPa
    // DÜZELTME: Doðru irtifa hesaplama formülü
    altitude = 44330.0 * (1.0 - pow(pressure / initialPressure, 0.1903));
    
    // Payload için hýz hesaplama
    unsigned long currentTime = millis();
    unsigned long deltaTime = currentTime - payloadPreviousTime;
    if (deltaTime > 0 && payloadPreviousTime > 0) {
      speed = (altitude - payloadPreviousAltitude) * 1000.0 / deltaTime; // m/s
    } else {
      speed = 0;
    }
    
    payloadPreviousTime = currentTime;
    payloadPreviousAltitude = altitude;
  }
  
  // TH08'den sýcaklýk ve nem verileri
  temperature = readTH08Temperature();
  if (temperature == 25.0) { // TH08 hatasý varsa BMP388 kullan
    if (bmp.performReading()) {
      temperature = bmp.temperature;
    }
  }
  float humidity = readTH08Humidity();
  
  // GPS verisi
  float gpsAltitude = gps.altitude.meters();
  float latitude = gps.location.lat();
  float longitude = gps.location.lng();
  
  // DÜZELTME: C# ParsePayloadData ile uyumlu sýralama
  memcpy(packet + offset, &altitude, 4);            offset += 4; // PayloadAltitude (offset 5) ?
  memcpy(packet + offset, &gpsAltitude, 4);         offset += 4; // PayloadGpsAltitude (offset 9)
  memcpy(packet + offset, &latitude, 4);            offset += 4; // PayloadLatitude (offset 13)
  memcpy(packet + offset, &longitude, 4);           offset += 4; // PayloadLongitude (offset 17)
  memcpy(packet + offset, &speed, 4);               offset += 4; // PayloadSpeed (offset 21)
  memcpy(packet + offset, &temperature, 4);         offset += 4; // PayloadTemperature (offset 25)
  memcpy(packet + offset, &pressure, 4);            offset += 4; // PayloadPressure (offset 29)
  memcpy(packet + offset, &humidity, 4);            offset += 4; // PayloadHumidity (offset 33)
  
  // CRC hesaplama (Header hariç) - offset 37
  byte crc = calculateCRC(packet + 4, offset - 4);
  packet[offset++] = crc;
  
  // LoRa ile gönder
  LoRa.beginPacket();
  LoRa.write(packet, PAYLOAD_PACKET_SIZE);
  LoRa.endPacket();
  
  // Binary paketi Serial'e de gönder (PC için)
  Serial.write(packet, PAYLOAD_PACKET_SIZE);
  
  // Debug mesajý
  Serial.print("Payload paketi #");
  Serial.print(payloadPacketCounter - 1);
  Serial.print(" - Ýrtifa: ");
  Serial.print(altitude);
  Serial.print("m, Sýcaklýk: ");
  Serial.print(temperature);
  Serial.print("°C, Nem: ");
  Serial.print(humidity);
  Serial.println("%");
}

void sendHYITelemetry() {
  byte packet[HYI_PACKET_SIZE];
  int offset = 0;
  
  // Header (4 byte)
  for (int i = 0; i < 4; i++) {
    packet[offset++] = HYI_HEADER[i];
  }
  
  // Team ID (1 byte)
  packet[offset++] = TEAM_ID;
  
  // Packet counter (1 byte)
  packet[offset++] = hyiPacketCounter++;
  
  // BMP388'den irtifa deðeri
  float altitude = 0;
  float pressure = 0;
  
  if (bmp.performReading()) {
    pressure = bmp.pressure / 100.0; // hPa
    // DÜZELTME: Doðru irtifa hesaplama formülü
    altitude = 44330.0 * (1.0 - pow(pressure / initialPressure, 0.1903));
  }
  
  // GPS verileri
  float rocketGpsAltitude = gps.altitude.meters();
  float rocketLatitude = gps.location.lat();
  float rocketLongitude = gps.location.lng();
  
  // Payload GPS verileri (bu örnekte roket verileri ile ayný)
  float payloadGpsAltitude = rocketGpsAltitude * 0.95;
  float payloadLatitude = rocketLatitude + 0.0001;
  float payloadLongitude = rocketLongitude + 0.0001;
  
  // Stage GPS verileri
  float stageGpsAltitude = rocketGpsAltitude * 0.5;
  float stageLatitude = rocketLatitude + 0.0002;
  float stageLongitude = rocketLongitude + 0.0002;
  
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
  byte status = 1; // Normal durum
  
  // HYI telemetri verileri (17 float + 1 byte status + 1 byte CRC + 2 byte padding = 78 byte)
  memcpy(packet + offset, &altitude, 4);           offset += 4;  // 1
  memcpy(packet + offset, &rocketGpsAltitude, 4);  offset += 4;  // 2
  memcpy(packet + offset, &rocketLatitude, 4);     offset += 4;  // 3
  memcpy(packet + offset, &rocketLongitude, 4);    offset += 4;  // 4
  memcpy(packet + offset, &payloadGpsAltitude, 4); offset += 4;  // 5
  memcpy(packet + offset, &payloadLatitude, 4);    offset += 4;  // 6
  memcpy(packet + offset, &payloadLongitude, 4);   offset += 4;  // 7
  memcpy(packet + offset, &stageGpsAltitude, 4);   offset += 4;  // 8
  memcpy(packet + offset, &stageLatitude, 4);      offset += 4;  // 9
  memcpy(packet + offset, &stageLongitude, 4);     offset += 4;  // 10
  memcpy(packet + offset, &gyroX, 4);              offset += 4;  // 11
  memcpy(packet + offset, &gyroY, 4);              offset += 4;  // 12
  memcpy(packet + offset, &gyroZ, 4);              offset += 4;  // 13
  memcpy(packet + offset, &accelX, 4);             offset += 4;  // 14
  memcpy(packet + offset, &accelY, 4);             offset += 4;  // 15
  memcpy(packet + offset, &accelZ, 4);             offset += 4;  // 16
  memcpy(packet + offset, &angle, 4);              offset += 4;  // 17
  packet[offset++] = status;                                     // 1 byte
  
  // CRC hesaplama
  byte crc = calculateCRC(packet + 4, offset - 4);
  packet[offset++] = crc;                                        // 1 byte
  
  // Son 2 byte padding (78 byte tamamlamak için)
  packet[offset++] = 0x00;
  packet[offset++] = 0x00;
  
  // LoRa ile gönder
  LoRa.beginPacket();
  LoRa.write(packet, HYI_PACKET_SIZE);
  LoRa.endPacket();
  
  // Binary paketi Serial'e de gönder (PC için)
  Serial.write(packet, HYI_PACKET_SIZE);
  
  // Debug mesajý
  Serial.print("HYI paketi #");
  Serial.print(hyiPacketCounter - 1);
  Serial.print(" - Ýrtifa: ");
  Serial.print(altitude);
  Serial.println("m");
}

// Basit CRC hesaplama (XOR metodu)
byte calculateCRC(byte* data, int length) {
  byte crc = 0;
  for (int i = 0; i < length; i++) {
    crc ^= data[i];
  }
  return crc;
}