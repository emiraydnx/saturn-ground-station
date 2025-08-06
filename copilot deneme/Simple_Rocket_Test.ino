/*
  Basit Arduino Roket Test Kodu
  
  Bu kod sadece roket telemetri verilerini gönderir.
  C# SerialPortService ile uyumlu 64 byte paket formatý kullanýr.
  
  Kullaným:
  1. Arduino IDE'de bu kodu yükleyin
  2. Serial Monitor'ü açýn (9600 baud)
  3. C# uygulamanýzda bu COM portunu dinleyin
*/

// Roket telemetri paketi - 64 byte (C# ile uyumlu)
uint8_t rocketPacket[64];

// Test deðiþkenleri
uint8_t paketNo = 0;
float irtifa = 0;
float hiz = 0;
float sicaklik = 20.0;
bool yukseliyor = true;

void setup() {
  Serial.begin(9600);
  
  Serial.println("?? Basit Roket Test - Baþlatýldý");
  Serial.println("Paket formatý: 64 byte roket verisi");
  Serial.println("Header: 0xAB 0xBC 0x12 0x13");
  Serial.println("C# SerialPortService ile uyumlu");
  Serial.println("");
  
  delay(2000); // 2 saniye bekle
}

void loop() {
  // Test verilerini güncelle
  testVerileriniGuncelle();
  
  // Paketi oluþtur
  rocketPaketiOlustur();
  
  // Paketi gönder
  Serial.write(rocketPacket, 64);
  
  // Debug yazdýr
  debugYazdir();
  
  delay(1000); // 1 saniye bekle
  paketNo = (paketNo + 1) % 256;
}

void testVerileriniGuncelle() {
  // Basit roket simülasyonu
  if (yukseliyor) {
    irtifa += random(10, 20);    // 10-20m artýþ
    hiz += random(2, 5);         // Hýz artýþý
    if (irtifa > 800) {          // 800m'de zirve
      yukseliyor = false;
    }
  } else {
    irtifa -= random(5, 15);     // 5-15m azalýþ  
    hiz -= random(1, 3);         // Hýz azalýþý
    if (irtifa < 0) {
      irtifa = 0;
      hiz = 0;
      yukseliyor = true;         // Yeniden baþla
    }
  }
  
  // Sýcaklýk irtifa ile deðiþir
  sicaklik = 20.0 - (irtifa * 0.006);
}

void rocketPaketiOlustur() {
  // Tüm paketi sýfýrla
  memset(rocketPacket, 0, 64);
  
  int index = 0;
  
  // Header (4 byte): 0xAB, 0xBC, 0x12, 0x13
  rocketPacket[index++] = 0xAB;
  rocketPacket[index++] = 0xBC;
  rocketPacket[index++] = 0x12;
  rocketPacket[index++] = 0x13;
  
  // Paket numarasý (1 byte)
  rocketPacket[index++] = paketNo;
  
  // Ýrtifa (4 byte float) - index 5
  floatYaz(irtifa, index);
  index += 4;
  
  // GPS Ýrtifa (4 byte float) - index 9
  floatYaz(irtifa + random(-2, 2), index);
  index += 4;
  
  // Enlem (4 byte float) - index 13
  floatYaz(39.925533 + random(-10, 10) / 1000000.0, index);
  index += 4;
  
  // Boylam (4 byte float) - index 17
  floatYaz(32.866287 + random(-10, 10) / 1000000.0, index);
  index += 4;
  
  // Jiroskop X,Y,Z (3x4 byte float) - index 21, 25, 29
  floatYaz(random(-50, 50), index); index += 4; // Gyro X
  floatYaz(random(-50, 50), index); index += 4; // Gyro Y  
  floatYaz(random(-50, 50), index); index += 4; // Gyro Z
  
  // Ývme X,Y,Z (3x4 byte float) - index 33, 37, 41
  floatYaz(random(-200, 200) / 100.0, index); index += 4; // Accel X (±2G)
  floatYaz(random(-200, 200) / 100.0, index); index += 4; // Accel Y (±2G)
  floatYaz(9.81 + random(-100, 100) / 100.0, index); index += 4; // Accel Z
  
  // Açý (4 byte float) - index 45
  floatYaz(random(-180, 180), index);
  index += 4;
  
  // Sýcaklýk (4 byte float) - index 49
  floatYaz(sicaklik, index);
  index += 4;
  
  // Basýnç (4 byte float) - index 53
  floatYaz(1013.25 - (irtifa * 0.12), index);
  index += 4;
  
  // Hýz (4 byte float) - index 57
  floatYaz(hiz, index);
  index += 4;
  
  // Durum (1 byte) - index 61
  rocketPacket[index++] = yukseliyor ? 2 : 3; // 2=Yükseliyor, 3=Alçalýyor
  
  // CRC (1 byte) - index 62 - C# ile uyumlu XOR hesaplama
  uint8_t crc = 0;
  for (int i = 4; i < 62; i++) {  // Paket counter'dan status'a kadar (4-61 dahil)
    crc ^= rocketPacket[i];
  }
  rocketPacket[index++] = crc;
  
  // Team ID son pozisyonda deðil, sabit 255 olarak set edilmiyor
  // C# koduna göre TeamID ayrý bir field deðil, sabit deðer
  // Paket 64 byte olmalý, son byte kullanýlmýyor
}

void floatYaz(float deger, int index) {
  // Float'ý byte array'e yaz (little endian)
  uint8_t* ptr = (uint8_t*)&deger;
  rocketPacket[index] = ptr[0];
  rocketPacket[index + 1] = ptr[1];
  rocketPacket[index + 2] = ptr[2];
  rocketPacket[index + 3] = ptr[3];
}

void debugYazdir() {
  Serial.print("?? #");
  Serial.print(paketNo);
  Serial.print(" | ");
  Serial.print(irtifa, 1);
  Serial.print("m | ");
  Serial.print(hiz, 1);
  Serial.print("m/s | ");
  Serial.print(sicaklik, 1);
  Serial.print("°C | ");
  Serial.print(yukseliyor ? "?? YUKSELÝYOR" : "?? ALCALIYOR");
  Serial.print(" | CRC: 0x");
  Serial.print(rocketPacket[62], HEX);
  Serial.print(" | Paket boyutu: ");
  Serial.println("64 byte");
}