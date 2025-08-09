/*
 * ?? LoRa VERÝCÝ ÝSTASYONU - ROKET TELEMETRÝ VERÝCÝ
 * 
 * Bu kod roket/payload tarafýnda çalýþýr ve sensör verilerini
 * LoRa üzerinden alýcý istasyona gönderir.
 * 
 * ? PAKET TÜRLERÝ (C# SerialPortService ile uyumlu):
 * - ROCKET PAKETI: 64 byte (sensör verileri)
 * - PAYLOAD PAKETI: 34 byte (payload verileri) 
 * - HYI PAKETÝ: 78 byte (komple telemetri)
 * 
 * ?? DONANIM:
 * - ESP32/Arduino
 * - LoRa SX1278 modülü
 * - BMP388 (basýnç/sýcaklýk)
 * - BNO055 (jiroskop/ivme)
 * - TH08 (sýcaklýk/nem)
 * - GPS modülü
 * 
 * ?? ÖZELLÝKLER:
 * - Gerçek sensör okuma
 * - Otomatik paket gönderimi
 * - C# sistemi ile uyumlu paketleme
 * - CRC kontrolü
 * - Debug çýktýlarý
 */

#include <SPI.h>
#include <LoRa.h>
#include <Wire.h>
#include <Adafruit_Sensor.h>
#include <Adafruit_BNO055.h>
#include <Adafruit_BMP3XX.h>
#include <TinyGPSPlus.h>

// ? LoRa PIN BAÐLANTILARI (ESP32 için)
#define LoRa_SCK    18  // SPI CLK
#define LoRa_MISO   19  // SPI MISO  
#define LoRa_MOSI   23  // SPI MOSI
#define LoRa_CS     5   // LoRa Chip Select
#define LoRa_RST    14  // LoRa Reset
#define LoRa_DIO0   2   // LoRa DIO0 (Interrupt)

// TH08 sensörü I2C adresi
#define TH08_I2C_ADDRESS 0x40

// GPS Serial tanýmlamasý
#define GPS_SERIAL Serial2

// ? C# SÝSTEMÝ ÝLE UYUMLU PAKET SABITLERI
const byte ROCKET_HEADER[4] = {0xAB, 0xBC, 0x12, 0x13};
const int ROCKET_PACKET_SIZE = 64;

const byte PAYLOAD_HEADER[4] = {0xCD, 0xDF, 0x14, 0x15};
const int PAYLOAD_PACKET_SIZE = 34;

const byte HYI_HEADER[4] = {0xFF, 0xFF, 0x54, 0x52};
const int HYI_PACKET_SIZE = 78;

// LoRa AYARLARI
const long LORA_FREQUENCY = 433E6;    // 433 MHz
const int LORA_SPREAD_FACTOR = 7;     // SF7 = hýzlý
const long LORA_BANDWIDTH = 125E3;    // 125 kHz
const int LORA_CODING_RATE = 5;       // 4/5
const int LORA_TX_POWER = 20;         // 20 dBm

// PAKET GÖNDERÝM AYARLARI
unsigned long rocketPacketInterval = 2000;  // 2 saniye
unsigned long payloadPacketInterval = 5000; // 5 saniye
unsigned long hyiPacketInterval = 10000;    // 10 saniye

unsigned long lastRocketPacketTime = 0;
unsigned long lastPayloadPacketTime = 0;
unsigned long lastHyiPacketTime = 0;

// PAKET SAYAÇLARI
byte rocketPacketCounter = 0;
byte payloadPacketCounter = 0;
byte hyiPacketCounter = 0;

// TEAM ID
const byte TEAM_ID = 123;

// Sensör nesneleri
Adafruit_BNO055 bno = Adafruit_BNO055(55);
Adafruit_BMP3XX bmp;
TinyGPSPlus gps;

// Sensör durumlarý
bool bmpAvailable = false;
bool bnoAvailable = false;
bool th08Available = false;

// Ýlk basýnç deðeri (irtifa hesabý için)
float initialPressure = 1013.25;
float previousAltitude = 0;
float previousPressure = 0;
unsigned long previousTime = 0;

// Test modu (gerçek sensörler yoksa)
bool testMode = false; // Varsayýlan olarak gerçek sensör modu

// Ýstatistikler
unsigned long totalPacketsSent = 0;
unsigned long startTime = 0;

// Sensör verisi yapýlarý
struct RocketSensorData {
  float rocketAltitude;
  float rocketGpsAltitude;
  float rocketLatitude;
  float rocketLongitude;
  float gyroX, gyroY, gyroZ;
  float accelX, accelY, accelZ;
  float angle;
  float rocketTemperature;
  float rocketPressure;
  float rocketSpeed;
};

struct PayloadSensorData {
  float payloadAltitude;
  float payloadGpsAltitude;
  float payloadLatitude;
  float payloadLongitude;
  float payloadSpeed;
  float payloadTemperature;
  float payloadPressure;
  float payloadHumidity;
};

struct HyiTestData {
  float altitude;
  float rocketGpsAltitude;
  float rocketLatitude;
  float rocketLongitude;
  float payloadGpsAltitude;
  float payloadLatitude;
  float payloadLongitude;
  float stageGpsAltitude;
  float stageLatitude;
  float stageLongitude;
  float gyroscopeX;
  float gyroscopeY;
  float gyroscopeZ;
  float accelerationX;
  float accelerationY;
  float accelerationZ;
  float angle;
};

// ? C# ile uyumlu CRC hesaplama
byte calculateChecksumAddition(byte* data, int length) {
  int sum = 0;
  for (int i = 0; i < length; i++) {
    sum += data[i];
  }
  return (byte)(sum % 256);
}

// ? Payload için basit CRC (XOR)
byte calculateSimpleCRC(byte* data, int length) {
  byte crc = 0;
  for (int i = 0; i < length; i++) {
    crc ^= data[i];
  }
  return crc;
}

void setup() {
  Serial.begin(115200);
  GPS_SERIAL.begin(9600);
  Wire.begin();
  delay(2000);
  
  Serial.println("?? LoRa VERÝCÝ ÝSTASYONU BAÞLATILIYOR...");
  Serial.println("?? ROKET TELEMETRÝ VERÝCÝ v2.0 (Gerçek Sensörler)");
  Serial.println("? C# SerialPortService Uyumlu");
  Serial.println("==========================================");

  // LoRa baþlat
  SPI.begin(LoRa_SCK, LoRa_MISO, LoRa_MOSI, LoRa_CS);
  LoRa.setPins(LoRa_CS, LoRa_RST, LoRa_DIO0);

  Serial.print("?? LoRa baþlatýlýyor... ");
  if (!LoRa.begin(LORA_FREQUENCY)) {
    Serial.println("? BAÞARISIZ!");
    while (1) {
      delay(1000);
      Serial.println("? LoRa baþlatýlamadý!");
    }
  }
  Serial.println("? BAÞARILI!");

  // LoRa ayarlarý
  LoRa.setSpreadingFactor(LORA_SPREAD_FACTOR);
  LoRa.setSignalBandwidth(LORA_BANDWIDTH);
  LoRa.setCodingRate4(LORA_CODING_RATE);
  LoRa.setTxPower(LORA_TX_POWER);
  LoRa.setSyncWord(0x34);

  Serial.println("?? LoRa Ayarlarý:");
  Serial.print("   ?? Frekans: "); Serial.print(LORA_FREQUENCY / 1E6); Serial.println(" MHz");
  Serial.print("   ?? SF: "); Serial.println(LORA_SPREAD_FACTOR);
  Serial.print("   ?? Güç: "); Serial.print(LORA_TX_POWER); Serial.println(" dBm");
  
  // Sensörleri baþlat
  initializeSensors();
  
  Serial.println("==========================================");
  Serial.println("?? PAKET GÖNDERÝM PROGRAMI:");
  Serial.print("   ?? Roket paketi: Her "); Serial.print(rocketPacketInterval); Serial.println(" ms");
  Serial.print("   ??? Payload paketi: Her "); Serial.print(payloadPacketInterval); Serial.println(" ms");
  Serial.print("   ?? HYI paketi: Her "); Serial.print(hyiPacketInterval); Serial.println(" ms");
  Serial.println("==========================================");
  Serial.println("?? Paket gönderimi baþlýyor...");
  Serial.println();

  startTime = millis();
  previousTime = millis();
}

void loop() {
  unsigned long currentTime = millis();
  
  // GPS verilerini oku
  while (GPS_SERIAL.available() > 0) {
    gps.encode(GPS_SERIAL.read());
  }
  
  // Roket paketi gönder
  if (currentTime - lastRocketPacketTime >= rocketPacketInterval) {
    sendRocketPacket();
    lastRocketPacketTime = currentTime;
  }
  
  // Payload paketi gönder
  if (currentTime - lastPayloadPacketTime >= payloadPacketInterval) {
    sendPayloadPacket();
    lastPayloadPacketTime = currentTime;
  }
  
  // HYI paketi gönder
  if (currentTime - lastHyiPacketTime >= hyiPacketInterval) {
    sendHyiPacket();
    lastHyiPacketTime = currentTime;
  }
  
  // Seri komutlarý dinle
  handleSerialCommands();
  
  delay(10);
}

void initializeSensors() {
  Serial.println("?? Sensörler baþlatýlýyor...");
  
  // BNO055 Jiroskop/Ývme sensörü
  Serial.print("   ?? BNO055 baþlatýlýyor... ");
  if (bno.begin()) {
    bno.setExtCrystalUse(true);
    bnoAvailable = true;
    Serial.println("?");
  } else {
    Serial.println("? BNO055 bulunamadý!");
    bnoAvailable = false;
  }

  // BMP388 Basýnç/Sýcaklýk sensörü
  Serial.print("   ??? BMP388 baþlatýlýyor... ");
  if (bmp.begin_I2C()) {
    bmp.setTemperatureOversampling(BMP3_OVERSAMPLING_8X);
    bmp.setPressureOversampling(BMP3_OVERSAMPLING_4X);
    bmp.setIIRFilterCoeff(BMP3_IIR_FILTER_COEFF_3);
    bmp.setOutputDataRate(BMP3_ODR_50_HZ);
    
    // Ýlk basýnç kalibrasyon deðerini oku
    float pressureSum = 0;
    int validReads = 0;
    for (int i = 0; i < 10; i++) {
      if (bmp.performReading()) {
        pressureSum += bmp.pressure / 100.0;
        validReads++;
      }
      delay(50);
    }
    if (validReads > 0) {
      initialPressure = pressureSum / validReads;
      previousPressure = initialPressure;
      Serial.print("? (Ýlk basýnç: ");
      Serial.print(initialPressure, 2);
      Serial.println(" hPa)");
      bmpAvailable = true;
    } else {
      Serial.println("? BMP388 okuma hatasý!");
      bmpAvailable = false;
    }
  } else {
    Serial.println("? BMP388 bulunamadý!");
    bmpAvailable = false;
  }

  // TH08 Sýcaklýk/Nem sensörü
  Serial.print("   ?? TH08 baþlatýlýyor... ");
  initTH08();
  float testTemp = readTH08Temperature();
  if (testTemp > -40 && testTemp < 85) {
    th08Available = true;
    Serial.print("? (Test sýcaklýk: ");
    Serial.print(testTemp, 1);
    Serial.println("°C)");
  } else {
    Serial.println("? TH08 bulunamadý!");
    th08Available = false;
  }

  // GPS kontrol
  Serial.print("   ??? GPS modülü: ");
  if (GPS_SERIAL) {
    Serial.println("? Serial2 hazýr");
  } else {
    Serial.println("? Serial2 baþlatýlamadý!");
  }

  // Test modu kararý
  if (!bmpAvailable && !bnoAvailable && !th08Available) {
    testMode = true;
    Serial.println("?? Hiçbir sensör bulunamadý - TEST MODU aktif!");
  } else {
    testMode = false;
    Serial.println("? Gerçek sensör modu aktif!");
  }
  
  Serial.println("?? Sensör baþlatma tamamlandý!");
}

void initTH08() {
  Wire.beginTransmission(TH08_I2C_ADDRESS);
  Wire.write(0xFE);
  Wire.endTransmission();
  delay(15);
}

float readTH08Temperature() {
  Wire.beginTransmission(TH08_I2C_ADDRESS);
  Wire.write(0xF3);
  Wire.endTransmission();
  delay(50);
  
  Wire.requestFrom(TH08_I2C_ADDRESS, 3);
  if (Wire.available() >= 3) {
    uint16_t rawTemp = Wire.read() << 8;
    rawTemp |= Wire.read();
    Wire.read(); // CRC'yi oku ama kullanma
    return -46.85 + 175.72 * (float)rawTemp / 65536.0;
  }
  return -999.0; // Hata deðeri
}

float readTH08Humidity() {
  Wire.beginTransmission(TH08_I2C_ADDRESS);
  Wire.write(0xF5);
  Wire.endTransmission();
  delay(50);
  
  Wire.requestFrom(TH08_I2C_ADDRESS, 3);
  if (Wire.available() >= 3) {
    uint16_t rawHumidity = Wire.read() << 8;
    rawHumidity |= Wire.read();
    Wire.read(); // CRC'yi oku ama kullanma
    return -6.0 + 125.0 * (float)rawHumidity / 65536.0;
  }
  return -999.0; // Hata deðeri
}

void sendRocketPacket() {
  byte packet[ROCKET_PACKET_SIZE];
  memset(packet, 0, sizeof(packet)); // Sýfýrla
  int offset = 0;
  
  // Header (4 byte)
  memcpy(packet + offset, ROCKET_HEADER, 4);
  offset += 4;
  
  // Packet Counter (1 byte)
  packet[offset++] = rocketPacketCounter++;
  
  // Sensör verilerini oku
  RocketSensorData sensorData = getRocketSensorData();
  
  // ? C# PARSE SIRASI ÝLE VERÝLERÝ PAKET ÝÇÝNE YERLEÞTÝR
  // Byte 5-8: Roket Ýrtifa (float)
  memcpy(packet + offset, &sensorData.rocketAltitude, 4); offset += 4;
  
  // Byte 9-12: Roket GPS Ýrtifa (float)
  memcpy(packet + offset, &sensorData.rocketGpsAltitude, 4); offset += 4;
  
  // Byte 13-16: Roket Enlem (float)
  memcpy(packet + offset, &sensorData.rocketLatitude, 4); offset += 4;
  
  // Byte 17-20: Roket Boylam (float)
  memcpy(packet + offset, &sensorData.rocketLongitude, 4); offset += 4;
  
  // Byte 21-24: Jiroskop X (float)
  memcpy(packet + offset, &sensorData.gyroX, 4); offset += 4;
  
  // Byte 25-28: Jiroskop Y (float)
  memcpy(packet + offset, &sensorData.gyroY, 4); offset += 4;
  
  // Byte 29-32: Jiroskop Z (float)
  memcpy(packet + offset, &sensorData.gyroZ, 4); offset += 4;
  
  // Byte 33-36: Ývme X (float)
  memcpy(packet + offset, &sensorData.accelX, 4); offset += 4;
  
  // Byte 37-40: Ývme Y (float)
  memcpy(packet + offset, &sensorData.accelY, 4); offset += 4;
  
  // Byte 41-44: Ývme Z (float)
  memcpy(packet + offset, &sensorData.accelZ, 4); offset += 4;
  
  // Byte 45-48: Açý (float)
  memcpy(packet + offset, &sensorData.angle, 4); offset += 4;
  
  // Byte 49-52: Sýcaklýk (float)
  memcpy(packet + offset, &sensorData.rocketTemperature, 4); offset += 4;
  
  // Byte 53-56: Basýnç (float)
  memcpy(packet + offset, &sensorData.rocketPressure, 4); offset += 4;
  
  // Byte 57-60: Hýz (float)
  memcpy(packet + offset, &sensorData.rocketSpeed, 4); offset += 4;
  
  // Byte 61: Status (1 byte)
  packet[offset++] = 2; // Normal durum
  
  // Byte 62: CRC (1 byte) - Header hariç, 4'ten 61'e kadar (status dahil)
  byte crc = calculateChecksumAddition(packet + 4, 58);
  packet[offset++] = crc;
  
  // Byte 63: Padding (64 byte'a tamamla)
  packet[offset++] = 0x00;
  
  // LoRa ile gönder
  sendLoRaPacket(packet, ROCKET_PACKET_SIZE, "ROCKET");
  
  // Debug bilgisi
  Serial.print("?? ROKET #");
  Serial.print(rocketPacketCounter - 1);
  Serial.print(" - Ýrtifa: ");
  Serial.print(sensorData.rocketAltitude, 1);
  Serial.print("m, GPS: ");
  Serial.print(sensorData.rocketGpsAltitude, 1);
  Serial.print("m, Temp: ");
  Serial.print(sensorData.rocketTemperature, 1);
  Serial.print("°C, CRC: 0x");
  Serial.println(crc, HEX);
}

void sendPayloadPacket() {
  byte packet[PAYLOAD_PACKET_SIZE];
  memset(packet, 0, sizeof(packet));
  int offset = 0;
  
  // Header (4 byte)
  memcpy(packet + offset, PAYLOAD_HEADER, 4);
  offset += 4;
  
  // Packet Counter (1 byte)
  packet[offset++] = payloadPacketCounter++;
  
  // Payload sensör verileri
  PayloadSensorData sensorData = getPayloadSensorData();
  
  // ? C# PARSE SIRASI ÝLE VERÝLERÝ YERLEÞTÝR
  // Byte 5-8: Payload Ýrtifa (float)
  memcpy(packet + offset, &sensorData.payloadAltitude, 4); offset += 4;
  
  // Byte 9-12: Payload GPS Ýrtifa (float)
  memcpy(packet + offset, &sensorData.payloadGpsAltitude, 4); offset += 4;
  
  // Byte 13-16: Payload Enlem (float)
  memcpy(packet + offset, &sensorData.payloadLatitude, 4); offset += 4;
  
  // Byte 17-20: Payload Boylam (float)
  memcpy(packet + offset, &sensorData.payloadLongitude, 4); offset += 4;
  
  // Byte 21-24: Payload Hýz (float)
  memcpy(packet + offset, &sensorData.payloadSpeed, 4); offset += 4;
  
  // Byte 25-28: Payload Sýcaklýk (float)
  memcpy(packet + offset, &sensorData.payloadTemperature, 4); offset += 4;
  
  // Byte 29-32: Payload Basýnç (float)
  memcpy(packet + offset, &sensorData.payloadPressure, 4); offset += 4;
  
  // Byte 33-36: Payload Nem (float)
  memcpy(packet + offset, &sensorData.payloadHumidity, 4); offset += 4;
  
  // Byte 37: CRC (1 byte) - Header hariç, 4'ten 36'ya kadar
  byte crc = calculateSimpleCRC(packet + 4, 33);
  packet[offset++] = crc;
  
  // LoRa ile gönder
  sendLoRaPacket(packet, PAYLOAD_PACKET_SIZE, "PAYLOAD");
  
  Serial.print("??? PAYLOAD #");
  Serial.print(payloadPacketCounter - 1);
  Serial.print(" - Ýrtifa: ");
  Serial.print(sensorData.payloadAltitude, 1);
  Serial.print("m, Nem: ");
  Serial.print(sensorData.payloadHumidity, 1);
  Serial.print("%, CRC: 0x");
  Serial.println(crc, HEX);
}

void sendHyiPacket() {
  byte packet[HYI_PACKET_SIZE];
  memset(packet, 0, sizeof(packet));
  int offset = 0;
  
  // Header (4 byte)
  memcpy(packet + offset, HYI_HEADER, 4);
  offset += 4;
  
  // Team ID (1 byte)
  packet[offset++] = TEAM_ID;
  
  // Packet Counter (1 byte)
  packet[offset++] = hyiPacketCounter++;
  
  // HYI verilerini oluþtur
  HyiTestData hyiData = getHyiTestData();
  
  // ? C# PARSE SIRASI ÝLE HYI VERÝLERÝNÝ YERLEÞTÝR
  // Byte 7-10: Ýrtifa (float)
  memcpy(packet + offset, &hyiData.altitude, 4); offset += 4;
  
  // Byte 11-14: Roket GPS Ýrtifa (float)
  memcpy(packet + offset, &hyiData.rocketGpsAltitude, 4); offset += 4;
  
  // Byte 15-18: Roket Enlem (float)
  memcpy(packet + offset, &hyiData.rocketLatitude, 4); offset += 4;
  
  // Byte 19-22: Roket Boylam (float)
  memcpy(packet + offset, &hyiData.rocketLongitude, 4); offset += 4;
  
  // Byte 23-26: Payload GPS Ýrtifa (float)
  memcpy(packet + offset, &hyiData.payloadGpsAltitude, 4); offset += 4;
  
  // Byte 27-30: Payload Enlem (float)
  memcpy(packet + offset, &hyiData.payloadLatitude, 4); offset += 4;
  
  // Byte 31-34: Payload Boylam (float)
  memcpy(packet + offset, &hyiData.payloadLongitude, 4); offset += 4;
  
  // Byte 35-38: Kademe GPS Ýrtifa (float)
  memcpy(packet + offset, &hyiData.stageGpsAltitude, 4); offset += 4;
  
  // Byte 39-42: Kademe Enlem (float)
  memcpy(packet + offset, &hyiData.stageLatitude, 4); offset += 4;
  
  // Byte 43-46: Kademe Boylam (float)
  memcpy(packet + offset, &hyiData.stageLongitude, 4); offset += 4;
  
  // Byte 47-50: Jiroskop X (float)
  memcpy(packet + offset, &hyiData.gyroscopeX, 4); offset += 4;
  
  // Byte 51-54: Jiroskop Y (float)
  memcpy(packet + offset, &hyiData.gyroscopeY, 4); offset += 4;
  
  // Byte 55-58: Jiroskop Z (float)
  memcpy(packet + offset, &hyiData.gyroscopeZ, 4); offset += 4;
  
  // Byte 59-62: Ývme X (float)
  memcpy(packet + offset, &hyiData.accelerationX, 4); offset += 4;
  
  // Byte 63-66: Ývme Y (float)
  memcpy(packet + offset, &hyiData.accelerationY, 4); offset += 4;
  
  // Byte 67-70: Ývme Z (float)
  memcpy(packet + offset, &hyiData.accelerationZ, 4); offset += 4;
  
  // Byte 71-74: Açý (float)
  memcpy(packet + offset, &hyiData.angle, 4); offset += 4;
  
  // Byte 75: Status (1 byte)
  packet[offset++] = 3; // HYI durumu
  
  // Byte 76: CRC (1 byte) - Header hariç, 4'ten 74'e kadar (status dahil)
  byte crc = calculateChecksumAddition(packet + 4, 71);
  packet[offset++] = crc;
  
  // Byte 77: 0x0D (CR)
  packet[offset++] = 0x0D;
  
  // Byte 78: 0x0A (LF)
  packet[offset++] = 0x0A;
  
  // LoRa ile gönder
  sendLoRaPacket(packet, HYI_PACKET_SIZE, "HYI");
  
  Serial.print("?? HYI #");
  Serial.print(hyiPacketCounter - 1);
  Serial.print(" - Takým: ");
  Serial.print(TEAM_ID);
  Serial.print(", Ýrtifa: ");
  Serial.print(hyiData.altitude, 1);
  Serial.print("m, CRC: 0x");
  Serial.println(crc, HEX);
}

void sendLoRaPacket(byte* packet, int size, String packetType) {
  LoRa.beginPacket();
  LoRa.write(packet, size);
  LoRa.endPacket();
  
  totalPacketsSent++;
  
  Serial.print("?? ");
  Serial.print(packetType);
  Serial.print(" paketi gönderildi (");
  Serial.print(size);
  Serial.print(" byte) - Toplam: ");
  Serial.println(totalPacketsSent);
}

RocketSensorData getRocketSensorData() {
  RocketSensorData data;
  
  // ? GERÇEK SENSÖR VERÝLERÝNÝ OKU
  if (!testMode) {
    // Basýnç ve irtifa hesabý
    if (bmpAvailable && bmp.performReading()) {
      float currentPressure = bmp.pressure / 100.0; // hPa'ya çevir
      data.rocketPressure = currentPressure;
      data.rocketTemperature = bmp.temperature;
      
      // Barometrik irtifa hesabý
      data.rocketAltitude = 44330.0 * (1.0 - pow(currentPressure / initialPressure, 0.1903));
      
      // Hýz hesabý (irtifa deðiþimi / zaman)
      unsigned long currentTime = millis();
      unsigned long deltaTime = currentTime - previousTime;
      if (deltaTime > 100) { // En az 100ms bekle
        data.rocketSpeed = (data.rocketAltitude - previousAltitude) * 1000.0 / deltaTime; // m/s
        previousTime = currentTime;
        previousAltitude = data.rocketAltitude;
      } else {
        data.rocketSpeed = 0; // Çok kýsa süre, hýz hesaplanamaz
      }
    } else {
      // BMP sensörü yok, default deðerler
      data.rocketAltitude = 0;
      data.rocketPressure = initialPressure;
      data.rocketTemperature = 20.0;
      data.rocketSpeed = 0;
    }
    
    // TH08 sýcaklýk kontrolü (varsa BMP yerine kullan)
    if (th08Available) {
      float th08Temp = readTH08Temperature();
      if (th08Temp > -40 && th08Temp < 85) {
        data.rocketTemperature = th08Temp;
      }
    }
    
    // BNO055 jiroskop ve ivme
    if (bnoAvailable) {
      sensors_event_t orientationData, linearAccelData;
      bno.getEvent(&orientationData, Adafruit_BNO055::VECTOR_EULER);
      bno.getEvent(&linearAccelData, Adafruit_BNO055::VECTOR_LINEARACCEL);
      
      if (!isnan(orientationData.orientation.x)) {
        data.gyroX = orientationData.orientation.x;
        data.gyroY = orientationData.orientation.y;
        data.gyroZ = orientationData.orientation.z;
        data.angle = orientationData.orientation.x; // Ana açý
      } else {
        // BNO veri hatasý, default deðerler
        data.gyroX = 0;
        data.gyroY = 0;
        data.gyroZ = 0;
        data.angle = 0;
      }
      
      if (!isnan(linearAccelData.acceleration.x)) {
        data.accelX = linearAccelData.acceleration.x;
        data.accelY = linearAccelData.acceleration.y;
        data.accelZ = linearAccelData.acceleration.z;
      } else {
        // Ývme hatasý, default deðerler
        data.accelX = 0;
        data.accelY = 0;
        data.accelZ = 9.81; // Yerçekimi
      }
    } else {
      // BNO sensörü yok, default deðerler
      data.gyroX = 0;
      data.gyroY = 0;
      data.gyroZ = 0;
      data.accelX = 0;
      data.accelY = 0;
      data.accelZ = 9.81;
      data.angle = 0;
    }
    
    // GPS koordinatlarý
    if (gps.location.isValid()) {
      data.rocketLatitude = gps.location.lat();
      data.rocketLongitude = gps.location.lng();
    } else {
      // GPS geçersiz, Ankara default koordinatlarý
      data.rocketLatitude = 39.925533;
      data.rocketLongitude = 32.866287;
    }
    
    // GPS irtifa
    if (gps.altitude.isValid()) {
      data.rocketGpsAltitude = gps.altitude.meters();
    } else {
      // GPS irtifa geçersiz, barometrik irtifa + offset kullan
      data.rocketGpsAltitude = data.rocketAltitude + 2.0;
    }
    
  } else {
    // ? TEST MODU - Sahte veriler
    float time = millis() / 1000.0;
    
    data.rocketAltitude = 250.0 + sin(time * 0.1) * 50.0;
    data.rocketGpsAltitude = data.rocketAltitude + 2.0;
    data.rocketLatitude = 39.925533 + sin(time * 0.05) * 0.001;
    data.rocketLongitude = 32.866287 + cos(time * 0.05) * 0.001;
    data.gyroX = sin(time * 0.5) * 30.0;
    data.gyroY = cos(time * 0.3) * 25.0;
    data.gyroZ = sin(time * 0.7) * 20.0;
    data.accelX = sin(time * 1.2) * 5.0;
    data.accelY = cos(time * 1.1) * 4.0;
    data.accelZ = 9.81 + sin(time * 0.8) * 2.0;
    data.angle = fmod(time * 10.0, 360.0);
    data.rocketTemperature = 23.5 + sin(time * 0.1) * 2.0;
    data.rocketPressure = 1013.25 + sin(time * 0.2) * 10.0;
    data.rocketSpeed = 45.0 + sin(time * 0.3) * 15.0;
  }
  
  return data;
}

PayloadSensorData getPayloadSensorData() {
  PayloadSensorData data;
  
  if (!testMode) {
    // ? GERÇEK PAYLOAD VERÝLERÝ
    // Payload aslýnda ayrý bir cihaz olacak, þimdilik roket verilerinden türet
    RocketSensorData rocketData = getRocketSensorData();
    
    // Payload irtifa roket irtifasýndan biraz düþük
    data.payloadAltitude = rocketData.rocketAltitude - 20.0;
    data.payloadGpsAltitude = rocketData.rocketGpsAltitude - 15.0;
    
    // GPS koordinatlarý rokete yakýn ama farklý
    data.payloadLatitude = rocketData.rocketLatitude - 0.0005; // Biraz güneyde
    data.payloadLongitude = rocketData.rocketLongitude - 0.0005; // Biraz batýda
    
    // Payload hýzý roket hýzýndan düþük
    data.payloadSpeed = rocketData.rocketSpeed * 0.7;
    
    // Sýcaklýk benzer ama biraz farklý
    data.payloadTemperature = rocketData.rocketTemperature - 2.0;
    data.payloadPressure = rocketData.rocketPressure + 5.0;
    
    // Nem sensörü (TH08 varsa kullan)
    if (th08Available) {
      float th08Humidity = readTH08Humidity();
      if (th08Humidity >= 0 && th08Humidity <= 100) {
        data.payloadHumidity = th08Humidity;
      } else {
        data.payloadHumidity = 65.0; // Default nem
      }
    } else {
      data.payloadHumidity = 65.0; // Default nem
    }
    
  } else {
    // ? TEST MODU
    float time = millis() / 1000.0;
    
    data.payloadAltitude = 200.0 + sin(time * 0.08) * 30.0;
    data.payloadGpsAltitude = data.payloadAltitude + 1.5;
    data.payloadLatitude = 39.925000 + sin(time * 0.04) * 0.0008;
    data.payloadLongitude = 32.866000 + cos(time * 0.04) * 0.0008;
    data.payloadSpeed = 25.0 + sin(time * 0.25) * 10.0;
    data.payloadTemperature = 20.5 + sin(time * 0.15) * 3.0;
    data.payloadPressure = 1015.0 + sin(time * 0.18) * 8.0;
    data.payloadHumidity = 65.0 + sin(time * 0.12) * 5.0;
  }
  
  return data;
}

HyiTestData getHyiTestData() {
  HyiTestData data;
  
  // HYI paketi gerçek sensör verilerinden oluþturulur
  RocketSensorData rocketData = getRocketSensorData();
  PayloadSensorData payloadData = getPayloadSensorData();
  
  // Roket verileri
  data.altitude = rocketData.rocketAltitude;
  data.rocketGpsAltitude = rocketData.rocketGpsAltitude;
  data.rocketLatitude = rocketData.rocketLatitude;
  data.rocketLongitude = rocketData.rocketLongitude;
  
  // Payload verileri
  data.payloadGpsAltitude = payloadData.payloadGpsAltitude;
  data.payloadLatitude = payloadData.payloadLatitude;
  data.payloadLongitude = payloadData.payloadLongitude;
  
  // Kademe verileri (roket verilerinden türetilir)
  data.stageGpsAltitude = rocketData.rocketAltitude - 50.0;
  data.stageLatitude = rocketData.rocketLatitude - 0.001;
  data.stageLongitude = rocketData.rocketLongitude - 0.001;
  
  // Sensör verileri
  data.gyroscopeX = rocketData.gyroX;
  data.gyroscopeY = rocketData.gyroY;
  data.gyroscopeZ = rocketData.gyroZ;
  data.accelerationX = rocketData.accelX;
  data.accelerationY = rocketData.accelY;
  data.accelerationZ = rocketData.accelZ;
  data.angle = rocketData.angle;
  
  return data;
}

void handleSerialCommands() {
  if (Serial.available()) {
    String command = Serial.readStringUntil('\n');
    command.trim();
    command.toUpperCase();
    
    if (command == "STATS") {
      showStats();
    }
    else if (command == "TEST ON") {
      testMode = true;
      Serial.println("? Test modu AKTÝF");
    }
    else if (command == "TEST OFF") {
      testMode = false;
      Serial.println("? Test modu KAPALI - Gerçek sensörler kullanýlacak");
    }
    else if (command.startsWith("INTERVAL ROCKET ")) {
      int interval = command.substring(16).toInt();
      if (interval >= 1000) {
        rocketPacketInterval = interval;
        Serial.print("?? Roket paketi aralýðý: ");
        Serial.print(interval);
        Serial.println(" ms");
      }
    }
    else if (command.startsWith("INTERVAL PAYLOAD ")) {
      int interval = command.substring(17).toInt();
      if (interval >= 1000) {
        payloadPacketInterval = interval;
        Serial.print("??? Payload paketi aralýðý: ");
        Serial.print(interval);
        Serial.println(" ms");
      }
    }
    else if (command.startsWith("INTERVAL HYI ")) {
      int interval = command.substring(13).toInt();
      if (interval >= 1000) {
        hyiPacketInterval = interval;
        Serial.print("?? HYI paketi aralýðý: ");
        Serial.print(interval);
        Serial.println(" ms");
      }
    }
    else if (command == "SENSORS") {
      showSensorStatus();
    }
    else if (command == "HELP") {
      showHelp();
    }
  }
}

void showStats() {
  unsigned long uptime = (millis() - startTime) / 1000;
  
  Serial.println("?? ===== ÝSTATÝSTÝKLER =====");
  Serial.print("?? Çalýþma süresi: ");
  Serial.print(uptime);
  Serial.println(" saniye");
  
  Serial.print("?? Toplam gönderilen: ");
  Serial.println(totalPacketsSent);
  
  if (uptime > 0) {
    float packetsPerMinute = (totalPacketsSent * 60.0) / uptime;
    Serial.print("?? Paket/dakika: ");
    Serial.println(packetsPerMinute, 1);
  }
  
  Serial.print("?? Roket sayacý: ");
  Serial.println(rocketPacketCounter);
  Serial.print("??? Payload sayacý: ");
  Serial.println(payloadPacketCounter);
  Serial.print("?? HYI sayacý: ");
  Serial.println(hyiPacketCounter);
  
  Serial.print("?? Test modu: ");
  Serial.println(testMode ? "AKTÝF" : "KAPALI");
  
  Serial.println("============================");
}

void showSensorStatus() {
  Serial.println("?? ===== SENSÖR DURUMU =====");
  
  Serial.print("?? BNO055 (Jiroskop/Ývme): ");
  Serial.println(bnoAvailable ? "? Aktif" : "? Kapalý");
  
  Serial.print("??? BMP388 (Basýnç/Sýcaklýk): ");
  Serial.println(bmpAvailable ? "? Aktif" : "? Kapalý");
  if (bmpAvailable) {
    Serial.print("   Ýlk basýnç: ");
    Serial.print(initialPressure, 2);
    Serial.println(" hPa");
  }
  
  Serial.print("?? TH08 (Sýcaklýk/Nem): ");
  Serial.println(th08Available ? "? Aktif" : "? Kapalý");
  
  Serial.print("??? GPS: ");
  if (gps.location.isValid()) {
    Serial.print("? Aktif (");
    Serial.print(gps.satellites.value());
    Serial.println(" uydu)");
  } else {
    Serial.println("? Sinyal yok");
  }
  
  Serial.println("============================");
}

void showHelp() {
  Serial.println("?? ===== KOMUTLAR =====");
  Serial.println("STATS              - Ýstatistikleri göster");
  Serial.println("SENSORS            - Sensör durumunu göster");
  Serial.println("TEST ON/OFF        - Test modunu aç/kapat");
  Serial.println("INTERVAL ROCKET ms - Roket paketi aralýðý");
  Serial.println("INTERVAL PAYLOAD ms- Payload paketi aralýðý");
  Serial.println("INTERVAL HYI ms    - HYI paketi aralýðý");
  Serial.println("HELP               - Bu yardým menüsü");
  Serial.println("========================");
}