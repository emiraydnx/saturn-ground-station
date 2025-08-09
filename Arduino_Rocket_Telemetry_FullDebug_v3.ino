/*
 * ?? ROKET TELEMETRÝ SÝMÜLATÖRÜ - TAM DEBUGLU VERSÝYON v4
 * 
 * C# SerialPortService ile %100 uyumlu, tek sýra hex dump
 * 
 * ? DÜZELTÝLEN SORUNLAR:
 * - Pozisyon debug bilgileri düzeltildi
 * - Tek sýrada tam hex dump eklendi
 * - Offset hesaplama hatalarý giderildi
 * - Her byte'ýn pozisyonu doðru gösteriliyor
 */

#include <stdint.h>

// ?? TAM 64 BYTE PAKET SABITLERI
const byte ROCKET_HEADER[4] = {0xAB, 0xBC, 0x12, 0x13};
const int ROCKET_PACKET_SIZE = 64;  
const byte TEAM_ID = 123;

// Paket sayacý
byte rocketPacketCounter = 0;

// Zamanlama deðiþkenleri
unsigned long lastRocketSendTime = 0;
const unsigned long ROCKET_SEND_INTERVAL = 2000;  // 2 saniye

// ? C# SerialPortService ile ayný CRC algoritmasý
byte calculateChecksumAddition(byte* data, int length) {
  int sum = 0;
  for (int i = 0; i < length; i++) {
    sum += data[i];
  }
  return (byte)(sum % 256);
}

// ? TEK SIRA HEX DUMP - TÜM PAKETÝ GÖSTER
void printSingleLineHexDump(byte* data, int length) {
  Serial.print("?? TAM PAKET HEX (64 byte): ");
  for (int i = 0; i < length; i++) {
    if (data[i] < 0x10) Serial.print("0");
    Serial.print(data[i], HEX);
    Serial.print(" ");
    
    // Her 16 byte'da alt satýra geç (okunabilirlik için)
    if ((i + 1) % 16 == 0 && i < length - 1) {
      Serial.println();
      Serial.print("                              ");
    }
  }
  Serial.println();
}

// ? SADECE DEÐERLER TEK SATIRDA
void printAllValuesInOneLine() {
  Serial.print("?? VERÝLER: ");
  Serial.print("Sayaç=");
  Serial.print(rocketPacketCounter);
  Serial.print(" Ýrtifa=");
  Serial.print(234.7f + (rocketPacketCounter % 10), 1);
  Serial.print("m GPS=");
  Serial.print(236.2f + (rocketPacketCounter % 10), 1);
  Serial.print("m Lat=");
  Serial.print(39.925533f, 6);
  Serial.print(" Lon=");
  Serial.print(32.866287f, 6);
  Serial.print(" Gyro=");
  Serial.print(12.5f, 1);
  Serial.print(",");
  Serial.print(-8.3f, 1);
  Serial.print(",");
  Serial.print(15.7f, 1);
  Serial.print(" Accel=");
  Serial.print(2.1f, 1);
  Serial.print(",");
  Serial.print(-1.2f, 1);
  Serial.print(",");
  Serial.print(10.8f, 1);
  Serial.print(" Angle=");
  Serial.print(78.4f, 1);
  Serial.print(" Temp=");
  Serial.print(23.5f, 1);
  Serial.print("°C Press=");
  Serial.print(1013.2f, 1);
  Serial.print("hPa Speed=");
  Serial.print(45.8f, 1);
  Serial.print("m/s Status=");
  Serial.print(2);
  Serial.println();
}

void setup() {
  Serial.begin(115200);
  delay(2000);
  
  Serial.println("?? ROKET TELEMETRÝ SÝMÜLATÖRÜ v4 - TEK SIRA DEBUG");
  Serial.println("?? C# SerialPortService ile %100 uyumlu");
  Serial.println("?? Tam 64 byte paket formatý");
  Serial.println("?? Tek sýrada hex dump ile debug");
  Serial.println("==========================================");
  Serial.println();
}

void loop() {
  if (millis() - lastRocketSendTime >= ROCKET_SEND_INTERVAL) {
    sendRocketTelemetry();
    lastRocketSendTime = millis();
  }
  delay(10);
}

void sendRocketTelemetry() {
  byte packet[ROCKET_PACKET_SIZE];
  int offset = 0;
  
  Serial.println("?? ====== YENÝ ROKET PAKETÝ ======");
  
  // ? HEADER (4 byte): 0xAB, 0xBC, 0x12, 0x13
  for (int i = 0; i < 4; i++) {
    packet[offset++] = ROCKET_HEADER[i];
  }
  
  // ? PAKET SAYACI (1 byte) - Pozisyon 4
  packet[offset++] = rocketPacketCounter++;
  
  // ? VERÝ DEÐERLER
  float altitude = 234.7f + (rocketPacketCounter % 10);
  float gpsAltitude = 236.2f + (rocketPacketCounter % 10);
  float latitude = 39.925533f;
  float longitude = 32.866287f;
  float gyroX = 12.5f;
  float gyroY = -8.3f;
  float gyroZ = 15.7f;
  float accelX = 2.1f;
  float accelY = -1.2f;
  float accelZ = 10.8f;
  float angle = 78.4f;
  float temperature = 23.5f;
  float pressure = 1013.2f;
  float speed = 45.8f;
  byte status = 2;
  
  // ? TEK SATIRDA TÜM VERÝLER
  printAllValuesInOneLine();
  
  // ? FLOAT'LARI PAKETE EKLE (C# ile ayný sýrada)
  memcpy(packet + offset, &altitude, 4);           offset += 4;    // 5-8
  memcpy(packet + offset, &gpsAltitude, 4);        offset += 4;    // 9-12
  memcpy(packet + offset, &latitude, 4);           offset += 4;    // 13-16
  memcpy(packet + offset, &longitude, 4);          offset += 4;    // 17-20
  memcpy(packet + offset, &gyroX, 4);              offset += 4;    // 21-24
  memcpy(packet + offset, &gyroY, 4);              offset += 4;    // 25-28
  memcpy(packet + offset, &gyroZ, 4);              offset += 4;    // 29-32
  memcpy(packet + offset, &accelX, 4);             offset += 4;    // 33-36
  memcpy(packet + offset, &accelY, 4);             offset += 4;    // 37-40
  memcpy(packet + offset, &accelZ, 4);             offset += 4;    // 41-44
  memcpy(packet + offset, &angle, 4);              offset += 4;    // 45-48
  memcpy(packet + offset, &temperature, 4);        offset += 4;    // 49-52
  memcpy(packet + offset, &pressure, 4);           offset += 4;    // 53-56
  memcpy(packet + offset, &speed, 4);              offset += 4;    // 57-60
  
  // ? STATUS (1 byte) - Pozisyon 61
  packet[offset++] = status;
  
  // ? CRC HESAPLAMA (C# SerialPortService uyumlu)
  byte crc = calculateChecksumAddition(packet + 4, offset - 4);
  packet[offset++] = crc;
  
  // ? PADDING (64 byte'a tamamla)
  while (offset < ROCKET_PACKET_SIZE) {
    packet[offset++] = 0x00;
  }
  
  Serial.print("?? CRC: 0x");
  Serial.print(crc, HEX);
  Serial.print(" (");
  Serial.print(crc);
  Serial.print(") Toplam: ");
  Serial.print(ROCKET_PACKET_SIZE);
  Serial.println(" byte");
  
  // ? TEK SIRA TAM HEX DUMP
  printSingleLineHexDump(packet, ROCKET_PACKET_SIZE);
  
  // ? PAKET GÖNDERÝM
  Serial.write(packet, ROCKET_PACKET_SIZE);
  
  Serial.println("? Binary paket gönderildi!");
  Serial.println("==========================================");
  Serial.println();
}