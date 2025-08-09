/*
 * ?? LoRa ALICI ÝSTASYONU v2.0 - ROKET TELEMETRÝ ALICI
 * 
 * Bu kod yer istasyonunda çalýþýr ve roket/payload telemetri verilerini
 * LoRa üzerinden alýr, parse eder ve C# sistemine seri port üzerinden iletir.
 * 
 * ? PAKET TÜRLERÝ (C# SerialPortService ile uyumlu):
 * - ROCKET PAKETI: 64 byte (Header: 0xAB 0xBC 0x12 0x13)
 * - PAYLOAD PAKETI: 34 byte (Header: 0xCD 0xDF 0x14 0x15)  
 * - HYI PAKETI: 78 byte (Header: 0xFF 0xFF 0x54 0x52)
 * - HYIDenem PAKETI: 98 byte (Header: 0xDE 0xAD 0xBE 0xEF)
 * 
 * ?? DONANIM:
 * - ESP32/Arduino (Alýcý)
 * - LoRa SX1278 modülü
 * - USB seri baðlantý (C# sistemi ile)
 * 
 * ?? ÖZELLÝKLER:
 * - Otomatik paket alýmý ve forwarding
 * - C# sistemi ile uyumlu binary format
 * - CRC doðrulama
 * - Sinyal gücü (RSSI) ölçümü
 * - Ýstatistik tutma
 * - Debug çýktýlarý
 * - TEK SEFERDE PAKET GÖNDERÝMÝ (32x32 deðil)
 */

#include <SPI.h>
#include <LoRa.h>
#include <Wire.h>

// ? LoRa PIN BAÐLANTILARI (ESP32 için)
#define LoRa_SCK    18  // SPI CLK
#define LoRa_MISO   19  // SPI MISO  
#define LoRa_MOSI   23  // SPI MOSI
#define LoRa_CS     5   // LoRa Chip Select
#define LoRa_RST    14  // LoRa Reset
#define LoRa_DIO0   2   // LoRa DIO0 (Interrupt)

// ? Arduino Uno için alternatif pinler (comment/uncomment edin)
/*
#define LoRa_SCK    13  // SPI CLK
#define LoRa_MISO   12  // SPI MISO  
#define LoRa_MOSI   11  // SPI MOSI
#define LoRa_CS     10  // LoRa Chip Select
#define LoRa_RST    9   // LoRa Reset
#define LoRa_DIO0   2   // LoRa DIO0 (Interrupt)
*/

// ? C# SÝSTEMÝ ÝLE UYUMLU PAKET SABITLERI
const byte ROCKET_HEADER[4] = {0xAB, 0xBC, 0x12, 0x13};
const int ROCKET_PACKET_SIZE = 64;

const byte PAYLOAD_HEADER[4] = {0xCD, 0xDF, 0x14, 0x15};
const int PAYLOAD_PACKET_SIZE = 34;

const byte HYI_HEADER[4] = {0xFF, 0xFF, 0x54, 0x52};
const int HYI_PACKET_SIZE = 78;

const byte HYIDENEM_HEADER[4] = {0xDE, 0xAD, 0xBE, 0xEF};
const int HYIDENEM_PACKET_SIZE = 98;

// LoRa AYARLARI (VERÝCÝ ÝLE AYNI OLMALI)
const long LORA_FREQUENCY = 433E6;    // 433 MHz
const int LORA_SPREAD_FACTOR = 7;     // SF7 = hýzlý
const long LORA_BANDWIDTH = 125E3;    // 125 kHz
const int LORA_CODING_RATE = 5;       // 4/5
const int LORA_SYNC_WORD = 0x34;      // Verici ile ayný

// ÝSTATÝSTÝKLER
unsigned long totalPacketsReceived = 0;
unsigned long rocketPacketsReceived = 0;
unsigned long payloadPacketsReceived = 0;
unsigned long hyiPacketsReceived = 0;
unsigned long hyiDenemPacketsReceived = 0;
unsigned long crcErrors = 0;
unsigned long invalidPackets = 0;
unsigned long startTime = 0;
unsigned long lastPacketTime = 0;

// SÝNYAL KALÝTESÝ
int lastRSSI = 0;
float lastSNR = 0;
int minRSSI = 0;
int maxRSSI = -999;
int signalQualitySum = 0;
int signalQualityCount = 0;

// PAKET BUFFER
const int MAX_PACKET_SIZE = 128;
byte packetBuffer[MAX_PACKET_SIZE];

// AYARLAR
bool debugMode = true;
bool statisticsMode = false;
bool crcCheckEnabled = true;
bool forwardingEnabled = true;
bool rssiInfoEnabled = true;

// ? C# ile uyumlu CRC hesaplama fonksiyonlarý
byte calculateChecksumAddition(byte* data, int length) {
  int sum = 0;
  for (int i = 0; i < length; i++) {
    sum += data[i];
  }
  return (byte)(sum % 256);
}

byte calculateSimpleCRC(byte* data, int length) {
  byte crc = 0;
  for (int i = 0; i < length; i++) {
    crc ^= data[i];
  }
  return crc;
}

void setup() {
  Serial.begin(115200);
  delay(2000);
  
  Serial.println("?? LoRa ALICI ÝSTASYONU v2.0 BAÞLATILIYOR...");
  Serial.println("?? ROKET TELEMETRÝ ALICI - Geliþmiþ Versiyon");
  Serial.println("? C# SerialPortService Tam Uyumlu");
  Serial.println("?? TEK SEFERDE PAKET GÖNDERÝMÝ AKTÝF");
  Serial.println("==========================================");

  // LoRa baþlat
  SPI.begin(LoRa_SCK, LoRa_MISO, LoRa_MOSI, LoRa_CS);
  LoRa.setPins(LoRa_CS, LoRa_RST, LoRa_DIO0);

  Serial.print("?? LoRa baþlatýlýyor... ");
  if (!LoRa.begin(LORA_FREQUENCY)) {
    Serial.println("? BAÞARISIZ!");
    Serial.println("?? Lütfen LoRa modülü baðlantýlarýný kontrol edin!");
    while (1) {
      delay(1000);
      Serial.println("? LoRa baþlatýlamadý - Yeniden deneniyor...");
    }
  }
  Serial.println("? BAÞARILI!");

  // LoRa ayarlarý (verici ile ayný olmalý)
  LoRa.setSpreadingFactor(LORA_SPREAD_FACTOR);
  LoRa.setSignalBandwidth(LORA_BANDWIDTH);
  LoRa.setCodingRate4(LORA_CODING_RATE);
  LoRa.setSyncWord(LORA_SYNC_WORD);
  
  // Alýcý moduna ayarla
  LoRa.receive();

  Serial.println("?? LoRa Alýcý Ayarlarý:");
  Serial.print("   ?? Frekans: "); Serial.print(LORA_FREQUENCY / 1E6); Serial.println(" MHz");
  Serial.print("   ?? SF: "); Serial.println(LORA_SPREAD_FACTOR);
  Serial.print("   ?? BW: "); Serial.print(LORA_BANDWIDTH / 1E3); Serial.println(" kHz");
  Serial.print("   ?? Sync Word: 0x"); Serial.println(LORA_SYNC_WORD, HEX);
  
  Serial.println("==========================================");
  Serial.println("?? DESTEKLENEN PAKET TÜRLERI:");
  Serial.println("   ?? ROCKET:   64 byte (Header: AB BC 12 13)");
  Serial.println("   ??? PAYLOAD:  34 byte (Header: CD DF 14 15)");
  Serial.println("   ?? HYI:      78 byte (Header: FF FF 54 52)");
  Serial.println("   ? HYIDenem: 98 byte (Header: DE AD BE EF)");
  Serial.println("==========================================");
  Serial.println("?? LoRa dinleme baþlýyor...");
  Serial.println("?? 'HELP' yazýn komutlarý görmek için");
  Serial.println("==========================================");
  Serial.println();

  startTime = millis();
  lastPacketTime = millis();
}

void loop() {
  // LoRa paket kontrolü
  int packetSize = LoRa.parsePacket();
  
  if (packetSize) {
    handleIncomingPacket(packetSize);
  }
  
  // Otomatik istatistik gösterimi (30 saniyede bir)
  if (statisticsMode && (millis() - lastPacketTime > 30000)) {
    showQuickStats();
    lastPacketTime = millis();
  }
  
  // Seri komutlarý dinle
  handleSerialCommands();
  
  delay(1);
}

void handleIncomingPacket(int packetSize) {
  // Sinyal kalitesi bilgilerini al
  lastRSSI = LoRa.packetRssi();
  lastSNR = LoRa.packetSnr();
  
  // RSSI istatistikleri güncelle
  if (lastRSSI < minRSSI || minRSSI == 0) minRSSI = lastRSSI;
  if (lastRSSI > maxRSSI) maxRSSI = lastRSSI;
  signalQualitySum += lastRSSI;
  signalQualityCount++;
  
  // Paket boyutu kontrolü
  if (packetSize > MAX_PACKET_SIZE) {
    invalidPackets++;
    if (debugMode) {
      Serial.print("? Çok büyük paket: ");
      Serial.print(packetSize);
      Serial.print(" byte (max: ");
      Serial.print(MAX_PACKET_SIZE);
      Serial.println(")");
    }
    return;
  }
  
  // Paketi oku
  int bytesRead = 0;
  while (LoRa.available() && bytesRead < packetSize && bytesRead < MAX_PACKET_SIZE) {
    packetBuffer[bytesRead] = LoRa.read();
    bytesRead++;
  }
  
  if (bytesRead != packetSize) {
    invalidPackets++;
    if (debugMode) {
      Serial.print("? Paket okuma hatasý: ");
      Serial.print(bytesRead);
      Serial.print("/");
      Serial.print(packetSize);
      Serial.println(" byte");
    }
    return;
  }
  
  totalPacketsReceived++;
  lastPacketTime = millis();
  
  // Paket türünü belirle ve iþle
  String packetType = identifyPacketType(packetBuffer, packetSize);
  
  if (packetType == "ROCKET") {
    if (processRocketPacket(packetBuffer, packetSize)) {
      rocketPacketsReceived++;
      if (forwardingEnabled) {
        // ? TEK SEFERDE PAKET GÖNDERÝMÝ
        forwardCompletePacketToPC(packetBuffer, packetSize, "ROCKET");
      }
      
      if (debugMode) {
        showRocketPacketInfo(packetBuffer);
      }
    }
  }
  else if (packetType == "PAYLOAD") {
    if (processPayloadPacket(packetBuffer, packetSize)) {
      payloadPacketsReceived++;
      if (forwardingEnabled) {
        // ? TEK SEFERDE PAKET GÖNDERÝMÝ
        forwardCompletePacketToPC(packetBuffer, packetSize, "PAYLOAD");
      }
      
      if (debugMode) {
        showPayloadPacketInfo(packetBuffer);
      }
    }
  }
  else if (packetType == "HYI") {
    if (processHyiPacket(packetBuffer, packetSize)) {
      hyiPacketsReceived++;
      if (forwardingEnabled) {
        // ? TEK SEFERDE PAKET GÖNDERÝMÝ
        forwardCompletePacketToPC(packetBuffer, packetSize, "HYI");
      }
      
      if (debugMode) {
        showHyiPacketInfo(packetBuffer);
      }
    }
  }
  else if (packetType == "HYIDENEM") {
    if (processHyiDenemPacket(packetBuffer, packetSize)) {
      hyiDenemPacketsReceived++;
      if (forwardingEnabled) {
        // ? TEK SEFERDE PAKET GÖNDERÝMÝ
        forwardCompletePacketToPC(packetBuffer, packetSize, "HYIDENEM");
      }
      
      if (debugMode) {
        showHyiDenemPacketInfo(packetBuffer);
      }
    }
  }
  else {
    invalidPackets++;
    if (debugMode) {
      Serial.print("? Bilinmeyen paket türü (");
      Serial.print(packetSize);
      Serial.print(" byte) - Header: ");
      for (int i = 0; i < 4 && i < packetSize; i++) {
        Serial.print("0x");
        Serial.print(packetBuffer[i], HEX);
        Serial.print(" ");
      }
      Serial.println();
    }
  }
  
  // RSSI bilgisi göster
  if (rssiInfoEnabled && debugMode) {
    Serial.print("?? RSSI: ");
    Serial.print(lastRSSI);
    Serial.print(" dBm, SNR: ");
    Serial.print(lastSNR);
    Serial.println(" dB");
  }
}

String identifyPacketType(byte* packet, int size) {
  if (size < 4) return "UNKNOWN";
  
  // Header'larý kontrol et
  if (memcmp(packet, ROCKET_HEADER, 4) == 0 && size == ROCKET_PACKET_SIZE) {
    return "ROCKET";
  }
  else if (memcmp(packet, PAYLOAD_HEADER, 4) == 0 && size == PAYLOAD_PACKET_SIZE) {
    return "PAYLOAD";
  }
  else if (memcmp(packet, HYI_HEADER, 4) == 0 && size == HYI_PACKET_SIZE) {
    return "HYI";
  }
  else if (memcmp(packet, HYIDENEM_HEADER, 4) == 0 && size == HYIDENEM_PACKET_SIZE) {
    return "HYIDENEM";
  }
  
  return "UNKNOWN";
}

bool processRocketPacket(byte* packet, int size) {
  if (size != ROCKET_PACKET_SIZE) return false;
  
  if (crcCheckEnabled) {
    // CRC kontrolü (byte 62)
    byte receivedCRC = packet[62];
    byte calculatedCRC = calculateChecksumAddition(packet + 4, 58); // 4'ten 61'e kadar
    
    if (receivedCRC != calculatedCRC) {
      crcErrors++;
      if (debugMode) {
        Serial.print("? ROKET CRC hatasý! Alýnan: 0x");
        Serial.print(receivedCRC, HEX);
        Serial.print(", Hesaplanan: 0x");
        Serial.println(calculatedCRC, HEX);
      }
      return false;
    }
  }
  
  return true;
}

bool processPayloadPacket(byte* packet, int size) {
  if (size != PAYLOAD_PACKET_SIZE) return false;
  
  if (crcCheckEnabled) {
    // CRC kontrolü (byte 33)
    byte receivedCRC = packet[33];
    byte calculatedCRC = calculateSimpleCRC(packet + 4, 29); // 4'ten 32'ye kadar
    
    if (receivedCRC != calculatedCRC) {
      crcErrors++;
      if (debugMode) {
        Serial.print("? PAYLOAD CRC hatasý! Alýnan: 0x");
        Serial.print(receivedCRC, HEX);
        Serial.print(", Hesaplanan: 0x");
        Serial.println(calculatedCRC, HEX);
      }
      return false;
    }
  }
  
  return true;
}

bool processHyiPacket(byte* packet, int size) {
  if (size != HYI_PACKET_SIZE) return false;
  
  if (crcCheckEnabled) {
    // CRC kontrolü (byte 75)
    byte receivedCRC = packet[75];
    byte calculatedCRC = calculateChecksumAddition(packet + 4, 71); // 4'ten 74'e kadar
    
    if (receivedCRC != calculatedCRC) {
      crcErrors++;
      if (debugMode) {
        Serial.print("? HYI CRC hatasý! Alýnan: 0x");
        Serial.print(receivedCRC, HEX);
        Serial.print(", Hesaplanan: 0x");
        Serial.println(calculatedCRC, HEX);
      }
      return false;
    }
  }
  
  return true;
}

bool processHyiDenemPacket(byte* packet, int size) {
  if (size != HYIDENEM_PACKET_SIZE) return false;
  
  if (crcCheckEnabled) {
    // CRC kontrolü (byte 95)
    byte receivedCRC = packet[95];
    byte calculatedCRC = calculateChecksumAddition(packet + 4, 91); // 4'ten 94'e kadar
    
    if (receivedCRC != calculatedCRC) {
      crcErrors++;
      if (debugMode) {
        Serial.print("? HYIDenem CRC hatasý! Alýnan: 0x");
        Serial.print(receivedCRC, HEX);
        Serial.print(", Hesaplanan: 0x");
        Serial.println(calculatedCRC, HEX);
      }
      return false;
    }
  }
  
  return true;
}

// ? TEK SEFERDE PAKET GÖNDERÝMÝ - C# SÝSTEMÝ ÝÇÝN OPTÝMÝZE EDÝLDÝ
void forwardCompletePacketToPC(byte* packet, int size, String packetType) {
  // BINARY VERÝYÝ TEK SEFERDE C# SÝSTEMÝNE GÖNDER
  // 32x32 blok halinde deðil, komple paketi bir kerede gönder
  Serial.write(packet, size);
  Serial.flush(); // Veriyi hemen zorla gönder
  
  if (debugMode) {
    Serial.print("?? ");
    Serial.print(packetType);
    Serial.print(" paketi TEK SEFERDE gönderildi (");
    Serial.print(size);
    Serial.print(" byte) - RSSI: ");
    Serial.print(lastRSSI);
    Serial.println(" dBm");
  }
}

void showRocketPacketInfo(byte* packet) {
  byte packetCounter = packet[4];
  
  float altitude;
  memcpy(&altitude, packet + 5, 4);
  
  float latitude;
  memcpy(&latitude, packet + 13, 4);
  
  float longitude;
  memcpy(&longitude, packet + 17, 4);
  
  float temperature;
  memcpy(&temperature, packet + 49, 4);
  
  Serial.print("?? ROKET #");
  Serial.print(packetCounter);
  Serial.print(": Ýrtifa=");
  Serial.print(altitude, 1);
  Serial.print("m, Pos=");
  Serial.print(latitude, 6);
  Serial.print(",");
  Serial.print(longitude, 6);
  Serial.print(", Temp=");
  Serial.print(temperature, 1);
  Serial.print("°C, RSSI=");
  Serial.print(lastRSSI);
  Serial.println(" dBm");
}

void showPayloadPacketInfo(byte* packet) {
  byte packetCounter = packet[4];
  
  float altitude;
  memcpy(&altitude, packet + 5, 4);
  
  float humidity;
  memcpy(&humidity, packet + 29, 4);
  
  Serial.print("??? PAYLOAD #");
  Serial.print(packetCounter);
  Serial.print(": Ýrtifa=");
  Serial.print(altitude, 1);
  Serial.print("m, Nem=");
  Serial.print(humidity, 1);
  Serial.print("%, RSSI=");
  Serial.print(lastRSSI);
  Serial.println(" dBm");
}

void showHyiPacketInfo(byte* packet) {
  byte teamId = packet[4];
  byte packetCounter = packet[5];
  
  float altitude;
  memcpy(&altitude, packet + 6, 4);
  
  Serial.print("?? HYI #");
  Serial.print(packetCounter);
  Serial.print(" (Takým ");
  Serial.print(teamId);
  Serial.print("): Ýrtifa=");
  Serial.print(altitude, 1);
  Serial.print("m, RSSI=");
  Serial.print(lastRSSI);
  Serial.println(" dBm");
}

void showHyiDenemPacketInfo(byte* packet) {
  byte teamId = packet[4];
  byte packetCounter = packet[5];
  
  float rocketAltitude;
  memcpy(&rocketAltitude, packet + 6, 4);
  
  float payloadAltitude;
  memcpy(&payloadAltitude, packet + 63, 4);
  
  Serial.print("? HYIDenem #");
  Serial.print(packetCounter);
  Serial.print(" (Takým ");
  Serial.print(teamId);
  Serial.print("): Roket=");
  Serial.print(rocketAltitude, 1);
  Serial.print("m, Payload=");
  Serial.print(payloadAltitude, 1);
  Serial.print("m, RSSI=");
  Serial.print(lastRSSI);
  Serial.println(" dBm");
}

void handleSerialCommands() {
  if (Serial.available()) {
    String command = Serial.readStringUntil('\n');
    command.trim();
    command.toUpperCase();
    
    if (command == "STATS") {
      showDetailedStats();
    }
    else if (command == "DEBUG ON") {
      debugMode = true;
      Serial.println("? Debug modu AKTÝF");
    }
    else if (command == "DEBUG OFF") {
      debugMode = false;
      Serial.println("? Debug modu KAPALI");
    }
    else if (command == "STATS ON") {
      statisticsMode = true;
      Serial.println("? Otomatik istatistik modu AKTÝF");
    }
    else if (command == "STATS OFF") {
      statisticsMode = false;
      Serial.println("? Otomatik istatistik modu KAPALI");
    }
    else if (command == "CRC ON") {
      crcCheckEnabled = true;
      Serial.println("? CRC kontrolü AKTÝF");
    }
    else if (command == "CRC OFF") {
      crcCheckEnabled = false;
      Serial.println("? CRC kontrolü KAPALI");
    }
    else if (command == "FORWARD ON") {
      forwardingEnabled = true;
      Serial.println("? C# forwarding AKTÝF");
    }
    else if (command == "FORWARD OFF") {
      forwardingEnabled = false;
      Serial.println("? C# forwarding KAPALI");
    }
    else if (command == "RSSI ON") {
      rssiInfoEnabled = true;
      Serial.println("? RSSI bilgisi AKTÝF");
    }
    else if (command == "RSSI OFF") {
      rssiInfoEnabled = false;
      Serial.println("? RSSI bilgisi KAPALI");
    }
    else if (command == "RESET") {
      resetStatistics();
      Serial.println("?? Ýstatistikler sýfýrlandý");
    }
    else if (command == "SIGNAL") {
      showSignalQuality();
    }
    else if (command == "TEST") {
      performLoRaTest();
    }
    else if (command.startsWith("SF")) {
      int sf = command.substring(2).toInt();
      if (sf >= 7 && sf <= 12) {
        LoRa.setSpreadingFactor(sf);
        Serial.print("?? Spreading Factor deðiþtirildi: SF");
        Serial.println(sf);
      } else {
        Serial.println("? Geçersiz SF deðeri! (7-12 arasý)");
      }
    }
    else if (command == "HELP") {
      showHelp();
    }
    else {
      Serial.println("? Bilinmeyen komut! 'HELP' yazýn.");
    }
  }
}

void showDetailedStats() {
  unsigned long uptime = (millis() - startTime) / 1000;
  
  Serial.println("?? ===== DETAYLI ÝSTATÝSTÝKLER =====");
  Serial.print("?? Çalýþma süresi: ");
  Serial.print(uptime);
  Serial.println(" saniye");
  
  Serial.print("?? Toplam alýnan: ");
  Serial.println(totalPacketsReceived);
  
  Serial.print("?? Roket paketleri: ");
  Serial.println(rocketPacketsReceived);
  
  Serial.print("??? Payload paketleri: ");
  Serial.println(payloadPacketsReceived);
  
  Serial.print("?? HYI paketleri: ");
  Serial.println(hyiPacketsReceived);
  
  Serial.print("? HYIDenem paketleri: ");
  Serial.println(hyiDenemPacketsReceived);
  
  Serial.print("? CRC hatalarý: ");
  Serial.println(crcErrors);
  
  Serial.print("? Geçersiz paketler: ");
  Serial.println(invalidPackets);
  
  if (uptime > 0) {
    float packetsPerMinute = (totalPacketsReceived * 60.0) / uptime;
    Serial.print("?? Paket/dakika: ");
    Serial.println(packetsPerMinute, 1);
    
    float successRate = 0;
    if (totalPacketsReceived > 0) {
      successRate = ((float)(totalPacketsReceived - crcErrors - invalidPackets) / totalPacketsReceived) * 100.0;
    }
    Serial.print("? Baþarý oraný: ");
    Serial.print(successRate, 1);
    Serial.println("%");
  }
  
  Serial.print("?? Son RSSI: ");
  Serial.print(lastRSSI);
  Serial.println(" dBm");
  
  Serial.print("?? Son SNR: ");
  Serial.print(lastSNR);
  Serial.println(" dB");
  
  if (signalQualityCount > 0) {
    float avgRSSI = (float)signalQualitySum / signalQualityCount;
    Serial.print("?? Ortalama RSSI: ");
    Serial.print(avgRSSI, 1);
    Serial.println(" dBm");
  }
  
  Serial.println("===================================");
}

void showQuickStats() {
  Serial.print("?? Toplam: ");
  Serial.print(totalPacketsReceived);
  Serial.print(" | Roket: ");
  Serial.print(rocketPacketsReceived);
  Serial.print(" | Payload: ");
  Serial.print(payloadPacketsReceived);
  Serial.print(" | HYI: ");
  Serial.print(hyiPacketsReceived);
  Serial.print(" | HYIDenem: ");
  Serial.print(hyiDenemPacketsReceived);
  Serial.print(" | Hata: ");
  Serial.print(crcErrors + invalidPackets);
  Serial.print(" | RSSI: ");
  Serial.print(lastRSSI);
  Serial.println(" dBm");
}

void showSignalQuality() {
  Serial.println("?? ===== SÝNYAL KALÝTESÝ =====");
  Serial.print("?? Son RSSI: ");
  Serial.print(lastRSSI);
  Serial.println(" dBm");
  
  Serial.print("?? Son SNR: ");
  Serial.print(lastSNR);
  Serial.println(" dB");
  
  Serial.print("?? Min RSSI: ");
  Serial.print(minRSSI);
  Serial.println(" dBm");
  
  Serial.print("?? Max RSSI: ");
  Serial.print(maxRSSI);
  Serial.println(" dBm");
  
  if (signalQualityCount > 0) {
    float avgRSSI = (float)signalQualitySum / signalQualityCount;
    Serial.print("?? Ortalama RSSI: ");
    Serial.print(avgRSSI, 1);
    Serial.println(" dBm");
  }
  
  // Sinyal kalitesi deðerlendirmesi
  String quality;
  if (lastRSSI > -60) quality = "Mükemmel";
  else if (lastRSSI > -70) quality = "Ýyi";
  else if (lastRSSI > -80) quality = "Orta";
  else if (lastRSSI > -90) quality = "Zayýf";
  else quality = "Çok Zayýf";
  
  Serial.print("?? Sinyal kalitesi: ");
  Serial.println(quality);
  
  Serial.println("==============================");
}

void resetStatistics() {
  totalPacketsReceived = 0;
  rocketPacketsReceived = 0;
  payloadPacketsReceived = 0;
  hyiPacketsReceived = 0;
  hyiDenemPacketsReceived = 0;
  crcErrors = 0;
  invalidPackets = 0;
  minRSSI = 0;
  maxRSSI = -999;
  signalQualitySum = 0;
  signalQualityCount = 0;
  startTime = millis();
}

void performLoRaTest() {
  Serial.println("?? LoRa baðlantý testi baþlatýlýyor...");
  
  // Mevcut ayarlarý göster
  Serial.print("?? Frekans: ");
  Serial.print(LORA_FREQUENCY / 1E6);
  Serial.println(" MHz");
  
  Serial.print("?? SF: ");
  Serial.println(LoRa.getSpreadingFactor());
  
  Serial.print("?? BW: ");
  Serial.print(LoRa.getSignalBandwidth() / 1E3);
  Serial.println(" kHz");
  
  Serial.println("? Test tamamlandý! Alýcý modu devam ediyor.");
}

void showHelp() {
  Serial.println("?? ===== KOMUTLAR =====");
  Serial.println("STATS              - Detaylý istatistikleri göster");
  Serial.println("DEBUG ON/OFF       - Debug modunu aç/kapat");
  Serial.println("STATS ON/OFF       - Otomatik istatistik modu");
  Serial.println("CRC ON/OFF         - CRC kontrolünü aç/kapat");
  Serial.println("FORWARD ON/OFF     - C# forwarding aç/kapat");
  Serial.println("RSSI ON/OFF        - RSSI bilgisini aç/kapat");
  Serial.println("SIGNAL             - Sinyal kalitesi bilgisi");
  Serial.println("RESET              - Ýstatistikleri sýfýrla");
  Serial.println("TEST               - LoRa test");
  Serial.println("SF7-SF12           - Spreading Factor deðiþtir");
  Serial.println("HELP               - Bu yardým menüsü");
  Serial.println("========================");
  Serial.println("?? PAKET GÖNDERÝM MODU: TEK SEFERDE");
  Serial.println("   Tüm paketler komple olarak C# sistemine");
  Serial.println("   binary format ile tek seferde iletilir.");
  Serial.println("   (32x32 bloklar halinde DEÐÝL!)");
  Serial.println("========================");
}