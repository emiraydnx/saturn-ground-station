# ?? LoRa TELEMETRÝ SÝSTEMÝ KURULUM KILAVUZU

## ?? SÝSTEM ÖZET

Bu LoRa sistemi, mevcut C# SerialPortService uygulamanýzla tam uyumlu çalýþýr ve aþaðýdaki paketleri destekler:

- **?? ROCKET PAKETÝ**: 64 byte (Header: `AB BC 12 13`)
- **??? PAYLOAD PAKETÝ**: 34 byte (Header: `CD DF 14 15`)
- **?? HYI PAKETÝ**: 78 byte (Header: `FF FF 54 52`)
- **? HYIDenem PAKETÝ**: 98 byte (Header: `DE AD BE EF`)

## ?? DONANIM GEREKSÝNÝMLERÝ

### Alýcý Ýstasyon (Yer Ýstasyonu)
- **Mikrodenetleyici**: ESP32 DevKit veya Arduino Uno
- **LoRa Modülü**: SX1278 (Ra-02 modülü önerilir)
- **Anten**: 433MHz spiral anten (17.3cm)
- **USB Kablo**: Bilgisayara baðlantý için

### Verici Ýstasyon (Roket/Payload)
- **Mikrodenetleyici**: ESP32 DevKit (hafif ve güçlü)
- **LoRa Modülü**: SX1278 (Ra-02)
- **Anten**: 433MHz kýsa anten
- **Güç**: LiPo batarya (3.7V, min 1000mAh)
- **Sensörler**: BMP388, BNO055, GPS modülü (isteðe baðlý)

## ?? KABLOLAMA (ESP32 için)

### LoRa SX1278 Baðlantýlarý
```
LoRa Pin  ?  ESP32 Pin
------------------------
VCC       ?  3.3V
GND       ?  GND
SCK       ?  GPIO 18 (SPI CLK)
MISO      ?  GPIO 19 (SPI MISO)
MOSI      ?  GPIO 23 (SPI MOSI)
NSS/CS    ?  GPIO 5  (Chip Select)
RST       ?  GPIO 14 (Reset)
DIO0      ?  GPIO 2  (Interrupt)
```

### Arduino Uno için Alternatif
```
LoRa Pin  ?  Arduino Pin
--------------------------
VCC       ?  3.3V
GND       ?  GND
SCK       ?  Pin 13 (SPI CLK)
MISO      ?  Pin 12 (SPI MISO)
MOSI      ?  Pin 11 (SPI MOSI)
NSS/CS    ?  Pin 10 (Chip Select)
RST       ?  Pin 9  (Reset)
DIO0      ?  Pin 2  (Interrupt)
```

## ?? KÜTÜPHANE KURULUMU

Arduino IDE'de aþaðýdaki kütüphaneleri kurun:

1. **LoRa by Sandeep Mistry** (v0.8.0 veya üzeri)
   ```
   Sketch ? Include Library ? Manage Libraries
   "LoRa" ara ve kur
   ```

2. **SPI** (Arduino core ile gelir)

3. **Wire** (Arduino core ile gelir)

## ?? KURULUM ADIMLARÝ

### 1. Alýcý Ýstasyon Kurulumu

1. **Donaným Baðlantýlarý**: Yukarýdaki kablolama þemasýna göre baðlayýn
2. **Kod Yükleme**: `LoRa_Receiver_Station.ino` dosyasýný ESP32'ye yükleyin
3. **Test**: Serial Monitor'ý açýn (115200 baud)
4. **Baðlantý**: USB üzerinden bilgisayara baðlayýn

### 2. Verici Ýstasyon Kurulumu

1. **Donaným Baðlantýlarý**: Ayný kablolama þemasý
2. **Kod Yükleme**: `LoRa_Transmitter_Station.ino` dosyasýný yükleyin
3. **Test**: Serial Monitor ile test mesajlarýný kontrol edin
4. **Güç**: LiPo batarya baðlayýn (uçuþ için)

### 3. C# Uygulama Entegrasyonu

1. **Port Ayarý**: C# uygulamasýnda Input Port'u alýcý istasyonun COM portuna ayarlayýn
2. **Baud Rate**: 115200 olarak ayarlayýn
3. **Test**: LoRa verici çalýþýrken C# uygulamasý paketleri almalý

## ?? LoRa PARAMETRE OPTÝMÝZASYONU

### Kýsa Menzil - Hýzlý Ýletiþim
```cpp
const int LORA_SPREAD_FACTOR = 7;     // SF7 = hýzlý
const long LORA_BANDWIDTH = 250E3;    // 250 kHz = hýzlý
const int LORA_CODING_RATE = 5;       // 4/5 = az hata korumasý
```

### Uzun Menzil - Güvenli Ýletiþim
```cpp
const int LORA_SPREAD_FACTOR = 12;    // SF12 = uzun menzil
const long LORA_BANDWIDTH = 125E3;    // 125 kHz = uzun menzil
const int LORA_CODING_RATE = 8;       // 4/8 = güçlü hata korumasý
```

### Dengeli Ayar (Önerilen)
```cpp
const int LORA_SPREAD_FACTOR = 9;     // SF9 = orta
const long LORA_BANDWIDTH = 125E3;    // 125 kHz
const int LORA_CODING_RATE = 5;       // 4/5
```

## ?? MENZIL TAHMÝNLERÝ

| SF | Bandwidth | Menzil | Hýz | Pil Ömrü |
|----|-----------|--------|-----|-----------|
| SF7 | 250kHz | 2-5km | Hýzlý | Kýsa |
| SF9 | 125kHz | 5-10km | Orta | Orta |
| SF12 | 125kHz | 10-15km | Yavaþ | Uzun |

## ?? SORUN GÝDERME

### LoRa Baþlatma Sorunu
```
? BAÞARISIZ! LoRa modülü baðlantýsýný kontrol edin!
```
**Çözüm**:
- Kablolama kontrolü yapýn
- 3.3V güç kontrolü yapýn
- SPI baðlantýlarý kontrol edin

### Paket Alma Sorunu
```
?? Bilinmeyen paket türü! Header tanýnmadý.
```
**Çözüm**:
- Verici ve alýcý ayný frekansta mý kontrol edin
- Sync Word ayný mý kontrol edin
- SF, BW, CR parametreleri ayný mý kontrol edin

### Düþük RSSI
```
RSSI: -110 dBm (çok zayýf)
```
**Çözüm**:
- Anten baðlantýsý kontrol edin
- Mesafeyi azaltýn
- SF deðerini artýrýn (SF9 veya SF12)
- TX gücünü artýrýn (max 20 dBm)

## ?? SERÝ KOMUTLAR

### Alýcý Ýstasyon Komutlarý
```
STATS / S     - Ýstatistikleri göster
DEBUG ON / D  - Debug çýktýlarýný aç
DEBUG OFF     - Debug çýktýlarýný kapat
RSSI ON       - RSSI bilgisini aç
RSSI OFF      - RSSI bilgisini kapat
TEST          - LoRa baðlantý testi
RESET         - Ýstatistikleri sýfýrla
SF7-SF12      - Spreading Factor deðiþtir
HELP / H      - Yardým menüsü
```

### Verici Ýstasyon Komutlarý
```
STATS              - Ýstatistikleri göster
TEST ON/OFF        - Test modunu aç/kapat
INTERVAL ROCKET ms - Roket paketi aralýðý
INTERVAL PAYLOAD ms- Payload paketi aralýðý
HELP               - Yardým menüsü
```

## ?? PERFORMANS ÝPUÇLARI

### 1. Güç Tasarrufu (Roket/Payload için)
```cpp
// Paket gönderimi arasýnda uyku modu
LoRa.sleep();
esp_sleep_enable_timer_wakeup(2000000); // 2 saniye
esp_light_sleep_start();
```

### 2. Paket Kayýp Kontrol
```cpp
// Her pakete sýra numarasý ekle
static uint16_t packetSequence = 0;
// Paket baþýna sequence ekle
```

### 3. Adaptive Data Rate
```cpp
// RSSI'ye göre SF otomatik ayarla
if (rssi > -70) setSpreadingFactor(7);      // Yakýn - hýzlý
else if (rssi > -100) setSpreadingFactor(9); // Orta - dengeli  
else setSpreadingFactor(12);                 // Uzak - güvenli
```

## ?? SÝSTEM ENTEGRASYONUNDAKÝ YERÝ

```
[Roket Sensörleri] ? [LoRa Verici] ~~~ [LoRa Alýcý] ? [C# Uygulamasý]
                                      433MHz            USB Serial
```

1. **Roket tarafý**: Sensörlerden veri topla ? LoRa ile gönder
2. **Yer istasyonu**: LoRa ile al ? Serial port'a aktar
3. **C# uygulamasý**: Serial port'tan oku ? Parse et ? Chart'lara ekle

## ?? GÜVENLÝK ÖNERÝLERÝ

1. **Unique Sync Word**: Farklý takýmlarla karýþmamasý için
   ```cpp
   LoRa.setSyncWord(0x7B); // Takým ID'nize özel
   ```

2. **Þifreleme** (Ýsteðe baðlý):
   ```cpp
   // Basit XOR þifreleme ekleyebilirsiniz
   ```

3. **Frekans Kontrolü**: Türkiye'de 433MHz serbest band

## ?? KONTROL LÝSTESÝ

### Kurulum Öncesi
- [ ] LoRa modülleri test edildi
- [ ] Antenler takýldý
- [ ] Kablolama kontrol edildi
- [ ] Kütüphaneler kuruldu

### Ýlk Test
- [ ] Alýcý istasyon baþlatýldý
- [ ] Verici istasyon baþlatýldý
- [ ] Paket alýmý test edildi
- [ ] C# baðlantýsý test edildi

### Uçuþ Öncesi
- [ ] Verici güç testi yapýldý
- [ ] Menzil testi yapýldý
- [ ] Paket kayýp oraný < %5
- [ ] C# uygulamasý paketleri doðru parse ediyor

Bu kýlavuzla LoRa sisteminizi baþarýyla kurabilir ve roket telemetrinizi kablosuz olarak alabilirsiniz! ????