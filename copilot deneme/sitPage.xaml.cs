using copilot_deneme.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Globalization;
using System.Threading.Tasks;
using copilot_deneme.TelemetryData;
using copilot_deneme.Services;

namespace copilot_deneme
{
    /// <summary>
    /// SİT Telemetri verilerini ve GPS haritasını görüntüleyen sayfa
    /// </summary>
    public sealed partial class sitPage : Page
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private ChartViewModel _viewModel;
        private SerialPortService? _serialPortService;

        // İstatistik değişkenleri
        private float _maxAltitude = 0;
        private int _Counter = 0;
        private int _CRC = 0;
        private int _TeamID = 0;

        // GPS Harita değişkenleri
        private bool _isMapInitialized = false;
        private double _currentRocketLat = 38.535533;  // Ankara başlangıç konumu
        private double _currentRocketLon = 27.01620;
        private double _currentPayloadLat = 38.535675;
        private double _currentPayloadLon = 27.02199;

        public sitPage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _viewModel = new ChartViewModel();

            InitializeDisplay();
            InitializeThreeDWebView();
            InitializeGpsMap();
            
            System.Diagnostics.Debug.WriteLine("sitPage başlatıldı - SerialPortService bağlantısı bekleniyor");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // ✨ YENİ: Navigation olduğunda SerialPortService bağlantısını kontrol et
            ConnectToSerialPortService();
            
            System.Diagnostics.Debug.WriteLine("sitPage navigasyonu tamamlandı - Port durumu kontrol edildi");
        }

        private async void InitializeThreeDWebView()
        {
            try
            {
                await ThreeDWebView.EnsureCoreWebView2Async();
                string assetPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "assets");
                ThreeDWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "assets.local", assetPath, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                ThreeDWebView.NavigateToString(HtmlTemplate);
                System.Diagnostics.Debug.WriteLine("3D WebView başarıyla başlatıldı");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"3D WebView başlatma hatası: {ex.Message}");
            }
        }

        private void OnRotationDataReceived(float yaw, float pitch, float roll)
        {
            // Gelen verinin UI thread'inde işlendiğinden emin ol
            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    // 3D modeli güncellemek için mevcut metodunuzu çağırın
                    await UpdateRotationAsync(yaw, pitch, roll);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Rotation güncelleme hatası: {ex.Message}");
                }
            });
        }

        // Telemetri verisi geldikçe bu metodu çağırın:
        public async Task UpdateRotationAsync(float yaw, float pitch, float roll)
        {
            try
            {
                if (ThreeDWebView.CoreWebView2 == null)
                    return;

                // JS fonksiyonunu çağır
                string script = $"updateModelRotation({yaw.ToString(CultureInfo.InvariantCulture)}, " +
                                               $"{pitch.ToString(CultureInfo.InvariantCulture)}, " +
                                               $"{roll.ToString(CultureInfo.InvariantCulture)});";
                await ThreeDWebView.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"3D model rotation güncelleme hatası: {ex.Message}");
            }
        }

        private async void InitializeGpsMap()
        {
            try
            {
                await MapWebView.EnsureCoreWebView2Async();
                string mapHtml = CreateSitPageMapHtml();
                MapWebView.NavigateToString(mapHtml);
                _isMapInitialized = true;
                System.Diagnostics.Debug.WriteLine("sitPage GPS harita başarıyla başlatıldı");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"sitPage GPS harita başlatma hatası: {ex.Message}");
            }
        }

        private string CreateSitPageMapHtml()
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <title>Roket GPS Takip Sistemi - Ana Harita</title>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <link rel='stylesheet' href='https://unpkg.com/leaflet@1.9.4/dist/leaflet.css' />
    <script src='https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'></script>
    <style>
        body {{ margin: 0; padding: 0; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; }}
        #map {{ height: 100vh; width: 100%; }}
        .custom-div-icon {{ 
            background: none; 
            border: none; 
        }}
        .rocket-marker {{
            background: #ff4444;
            width: 18px;
            height: 18px;
            border-radius: 50%;
            border: 4px solid white;
            box-shadow: 0 0 8px rgba(255,68,68,0.9);
            animation: pulse-red 2s infinite;
        }}
        .payload-marker {{
            background: #44ff44;
            width: 18px;
            height: 18px;
            border-radius: 50%;
            border: 4px solid white;
            box-shadow: 0 0 8px rgba(68,255,68,0.9);
            animation: pulse-green 2s infinite;
        }}
        @keyframes pulse-red {{
            0% {{ box-shadow: 0 0 8px rgba(255,68,68,0.9); }}
            50% {{ box-shadow: 0 0 16px rgba(255,68,68,1), 0 0 24px rgba(255,68,68,0.6); }}
            100% {{ box-shadow: 0 0 8px rgba(255,68,68,0.9); }}
        }}
        @keyframes pulse-green {{
            0% {{ box-shadow: 0 0 8px rgba(68,255,68,0.9); }}
            50% {{ box-shadow: 0 0 16px rgba(68,255,68,1), 0 0 24px rgba(68,255,68,0.6); }}
            100% {{ box-shadow: 0 0 8px rgba(68,255,68,0.9); }}
        }}
        .legend-panel {{
            position: absolute;
            bottom: 15px;
            right: 15px;
            background: rgba(0,0,0,0.85);
            color: white;
            padding: 10px 14px;
            border-radius: 8px;
            font-size: 11px;
            z-index: 1000;
        }}
    </style>
</head>
<body>
    <div id='map'></div>

    <div class='legend-panel'>
        <div style='font-weight: bold; margin-bottom: 4px;'>Açıklama</div>
        <div>🔴 Roket Konumu</div>
        <div>🟢 Payload Konumu</div>
        <div>📍Uçuş Rotaları</div>
    </div>
    
    <script>
        // Harita oluştur - Ankara merkezi
        var map = L.map('map').setView([39.925533, 32.866287], 13);
        
        // OpenStreetMap tile layer ekle
        L.tileLayer('https://{{s}}.tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png', {{
            attribution: '© OpenStreetMap | SİT Telemetri Sistemi',
            maxZoom: 18
        }}).addTo(map);
        
        // Roket marker'ı (kırmızı)
        var rocketMarker = L.marker([39.925533, 32.866287], {{
            icon: L.divIcon({{
                html: '<div class=""rocket-marker""></div>',
                iconSize: [26, 26],
                className: 'custom-div-icon'
            }}),
            title: 'Roket Konumu'
        }}).addTo(map);
        
        // Payload marker'ı (yeşil)
        var payloadMarker = L.marker([39.925533, 32.866287], {{
            icon: L.divIcon({{
                html: '<div class=""payload-marker""></div>',
                iconSize: [26, 26],
                className: 'custom-div-icon'
            }}),
            title: 'Payload Konumu'
        }}).addTo(map);
        
        // Roket uçuş yolu (kırmızı çizgi)
        var rocketPath = L.polyline([], {{ 
            color: '#ff4444', 
            weight: 4, 
            opacity: 0.8,
            dashArray: '8, 8'
        }}).addTo(map);
        
        // Payload uçuş yolu (yeşil çizgi)
        var payloadPath = L.polyline([], {{ 
            color: '#44ff44', 
            weight: 4, 
            opacity: 0.8,
            dashArray: '8, 8'
        }}).addTo(map);
        
        // Tooltip'ler ekle
        rocketMarker.bindTooltip('Roket Aracı', {{ permanent: false, direction: 'top' }});
        payloadMarker.bindTooltip('Payload Aracı', {{ permanent: false, direction: 'top' }});
        
        // Mesafe hesaplama
        function calculateDistance(lat1, lon1, lat2, lon2) {{
            var R = 6371000; // Dünya yarıçapı metre cinsinden
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.sin(dLat/2) * Math.sin(dLat/2) +
                    Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
                    Math.sin(dLon/2) * Math.sin(dLon/2);
            var c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1-a));
            return R * c;
        }}
        
        // ✅ C# tarafından çağrılacak JavaScript fonksiyonları - GEÇERLİ KOORDİNAT KONTROLÜ İLE
        window.updateRocketPosition = function(lat, lon, alt) {{
            console.log('🚀 Roket pozisyon güncellemesi:', lat, lon, alt);
            if (lat !== 0 && lon !== 0 && !isNaN(lat) && !isNaN(lon) && Math.abs(lat) <= 90 && Math.abs(lon) <= 180) {{
                var newPos = [lat, lon];
                rocketMarker.setLatLng(newPos);
                rocketPath.addLatLng(newPos);
                fitMapToBounds();
                console.log('✅ Roket marker güncellendi:', lat.toFixed(6), lon.toFixed(6));
            }} else {{
                console.log('❌ Geçersiz roket koordinatları:', lat, lon);
            }}
        }};
        
        window.updatePayloadPosition = function(lat, lon, alt) {{
            console.log('🟢 Payload pozisyon güncellemesi:', lat, lon, alt);
            if (lat !== 0 && lon !== 0 && !isNaN(lat) && !isNaN(lon) && Math.abs(lat) <= 90 && Math.abs(lon) <= 180) {{
                var newPos = [lat, lon];
                payloadMarker.setLatLng(newPos);
                payloadPath.addLatLng(newPos);
                fitMapToBounds();
                console.log('✅ Payload marker güncellendi:', lat.toFixed(6), lon.toFixed(6));
            }} else {{
                console.log('❌ Geçersiz payload koordinatları:', lat, lon);
            }}
        }};
        
        window.updateBothPositions = function(rocketLat, rocketLon, rocketAlt, payloadLat, payloadLon, payloadAlt) {{
            console.log('🔄 Her iki pozisyon güncellemesi:', rocketLat, rocketLon, payloadLat, payloadLon);
            var updated = false;
            
            if (rocketLat !== 0 && rocketLon !== 0 && !isNaN(rocketLat) && !isNaN(rocketLon) && Math.abs(rocketLat) <= 90 && Math.abs(rocketLon) <= 180) {{
                var rocketPos = [rocketLat, rocketLon];
                rocketMarker.setLatLng(rocketPos);
                rocketPath.addLatLng(rocketPos);
                updated = true;
                console.log('✅ Roket güncellendi:', rocketLat.toFixed(6), rocketLon.toFixed(6));
            }} else {{
                console.log('❌ Geçersiz roket koordinatları - geçiliyor');
            }}
            
            if (payloadLat !== 0 && payloadLon !== 0 && !isNaN(payloadLat) && !isNaN(payloadLon) && Math.abs(payloadLat) <= 90 && Math.abs(payloadLon) <= 180) {{
                var payloadPos = [payloadLat, payloadLon];
                payloadMarker.setLatLng(payloadPos);
                payloadPath.addLatLng(payloadPos);
                updated = true;
                console.log('✅ Payload güncellendi:', payloadLat.toFixed(6), payloadLon.toFixed(6));
            }} else {{
                console.log('❌ Geçersiz payload koordinatları - geçiliyor');
            }}
            
            if (updated) {{
                fitMapToBounds();
            }}
        }};
        
        function fitMapToBounds() {{
            var group = new L.featureGroup([rocketMarker, payloadMarker]);
            var bounds = group.getBounds();
            if (bounds.isValid()) {{
                map.fitBounds(bounds.pad(0.15));
            }}
        }}
        
        // ✅ DEBUG İÇİN CONSOLE LOGLAMASINI AKTİF ET
        console.log('🌍 GPS Harita sistemi başlatıldı - Koordinat güncellemeleri bekleniyor...');
    </script>
</body>
</html>";
        }

        private async void UpdateSitPageGpsPositions(double rocketLat, double rocketLon, double rocketAlt, double payloadLat, double payloadLon, double payloadAlt)
        {
            if (!_isMapInitialized) return;
            
            try
            {
                // ✅ GEÇERLİ KOORDİNAT KONTROLÜ EKLE
                bool rocketValid = rocketLat != 0 && rocketLon != 0 && 
                                  !double.IsNaN(rocketLat) && !double.IsNaN(rocketLon) &&
                                  Math.Abs(rocketLat) <= 90 && Math.Abs(rocketLon) <= 180;
                                  
                bool payloadValid = payloadLat != 0 && payloadLon != 0 && 
                                   !double.IsNaN(payloadLat) && !double.IsNaN(payloadLon) &&
                                   Math.Abs(payloadLat) <= 90 && Math.Abs(payloadLon) <= 180;
                
                // Sadece geçerli koordinatları güncelle
                if (rocketValid)
                {
                    _currentRocketLat = rocketLat;
                    _currentRocketLon = rocketLon;
                    System.Diagnostics.Debug.WriteLine($"📍 GEÇERLİ Roket koordinatı: {rocketLat:F6}, {rocketLon:F6}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ GEÇERSİZ Roket koordinatı: {rocketLat:F6}, {rocketLon:F6}");
                }
                
                if (payloadValid)
                {
                    _currentPayloadLat = payloadLat;
                    _currentPayloadLon = payloadLon;
                    System.Diagnostics.Debug.WriteLine($"📍 GEÇERLİ Payload koordinatı: {payloadLat:F6}, {payloadLon:F6}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ GEÇERSİZ Payload koordinatı: {payloadLat:F6}, {payloadLon:F6}");
                }
                
                // ✅ DETAYLI DEBUG BİLGİSİ
                System.Diagnostics.Debug.WriteLine($"🔍 GPS Güncelleme çağrısı:");
                System.Diagnostics.Debug.WriteLine($"   - Roket: ({rocketLat:F6}, {rocketLon:F6}) - Geçerli: {rocketValid}");
                System.Diagnostics.Debug.WriteLine($"   - Payload: ({payloadLat:F6}, {payloadLon:F6}) - Geçerli: {payloadValid}");
                
                // JavaScript fonksiyonunu çağır - irtifa bilgisi ile birlikte
                string script = $"updateBothPositions({_currentRocketLat.ToString(CultureInfo.InvariantCulture)}, " +
                                   $"{_currentRocketLon.ToString(CultureInfo.InvariantCulture)}, " +
                                   $"{rocketAlt.ToString(CultureInfo.InvariantCulture)}, " +
                                   $"{_currentPayloadLat.ToString(CultureInfo.InvariantCulture)}, " +
                                   $"{_currentPayloadLon.ToString(CultureInfo.InvariantCulture)}, " +
                                   $"{payloadAlt.ToString(CultureInfo.InvariantCulture)})";
                
                // ✅ SCRIPT'İ ÇALIŞTIR VE LOG'LA
                System.Diagnostics.Debug.WriteLine($"🌍 JavaScript çağrılıyor: {script}");
                await MapWebView.ExecuteScriptAsync(script);
                
                System.Diagnostics.Debug.WriteLine($"✅ sitPage GPS pozisyonları güncellendi - Roket: {_currentRocketLat:F6}, {_currentRocketLon:F6} ({rocketAlt:F1}m) | Payload: {_currentPayloadLat:F6}, {_currentPayloadLon:F6} ({payloadAlt:F1}m)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ sitPage GPS pozisyon güncelleme hatası: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   - Stack Trace: {ex.StackTrace}");
            }
        }

        private const string HtmlTemplate = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"" />
  <title>3D Scene with STL</title>
  <style>
    body, html { margin: 0; padding: 0; overflow: hidden; background-color: #1e1e1e; }
    canvas { display: block; }
  </style>
</head>
<body>
  <script src=""https://cdn.jsdelivr.net/npm/three@0.142.0/build/three.min.js""></script>
  <script src=""https://cdn.jsdelivr.net/npm/three@0.142.0/examples/js/loaders/STLLoader.js""></script>
  
  <script>
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(50, window.innerWidth / window.innerHeight, 0.1, 1000);
    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(window.innerWidth, window.innerHeight);
    document.body.appendChild(renderer.domElement);

    const ambientLight = new THREE.AmbientLight(0xffffff, 0.7);
    const dirLight = new THREE.DirectionalLight(0xffffff, 1.0);
    dirLight.position.set(5, 10, 7.5);
    scene.add(ambientLight, dirLight);

    camera.position.set(5, 5, 5);
    camera.lookAt(0, 0, 0);
    camera.up.set(0, 1, 0);

    let model;
    const loader = new THREE.STLLoader();

   loader.load(
    'https://assets.local/rocket_model.stl',
    function (geometry) {
        geometry.computeBoundingBox();
        const center = new THREE.Vector3();
        geometry.boundingBox.getCenter(center).negate();
        geometry.translate(center.x, center.y, center.z);

        const material = new THREE.MeshStandardMaterial({
            color: 0x0077ff,
            metalness: 0.3,
            roughness: 0.5,
            side: THREE.DoubleSide
        });
        const mesh = new THREE.Mesh(geometry, material);

        // Otomatik scale (çok büyük/küçük model varsa normalize et)
        geometry.computeBoundingBox();
        const size = geometry.boundingBox.getSize(new THREE.Vector3());
        const maxDim = Math.max(size.x, size.y, size.z);
        const scale = 5.0 / maxDim;
        mesh.scale.set(scale, scale, scale);

        scene.add(mesh);
        model = mesh;

        console.log('STL model yüklendi ve ortalandı.');
    },
    function (xhr) { console.log((xhr.loaded / xhr.total * 100) + '% loaded'); },
    function (error) { console.error('STL yüklenirken hata:', error); }
);

    function animate() {
        requestAnimationFrame(animate);
        renderer.render(scene, camera);
    }
    animate();

    window.updateModelRotation = (yaw, pitch, roll) => {
        if (model) {
            model.rotation.y = yaw * Math.PI / 180;
            model.rotation.x = pitch * Math.PI / 180;
            model.rotation.z = roll * Math.PI / 180;
        }
    };

    window.addEventListener('resize', () => {
        camera.aspect = window.innerWidth / window.innerHeight;
        camera.updateProjectionMatrix();
        renderer.setSize(window.innerWidth, window.innerHeight);
    });
  </script>
</body>
</html>";

        private void InitializeDisplay()
        {
            // İlk değerleri ayarla
            LastUpdateText.Text = "Bağlantı bekleniyor...";
            DataCountText.Text = "0";
            MaxAltitudeText.Text = "0.0 m";
            CRCText.Text = "0";
            TeamIDText.Text = "0";
        }

        private void OnSerialDataReceived(string data)
        {
            // Bu sadece bağlantı durumunu göstermek için
            _dispatcherQueue.TryEnqueue(() =>
            {
                LastUpdateText.Text = $"Veri alıyor: {DateTime.Now:HH:mm:ss}";
            });
        }

        private void OnTelemetryDataUpdated(RocketTelemetryData rocketData, PayloadTelemetryData payloadData)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // ✅ DEBUG: Gelen veriyi analiz et
                    System.Diagnostics.Debug.WriteLine($"🔍 OnTelemetryDataUpdated çağrıldı:");
                    System.Diagnostics.Debug.WriteLine($"   - RocketData null mu: {rocketData == null}");
                    System.Diagnostics.Debug.WriteLine($"   - PayloadData null mu: {payloadData == null}");
                    
                    if (rocketData != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"   - Roket Koordinatları: {rocketData.RocketLatitude:F6}, {rocketData.RocketLongitude:F6}");
                        System.Diagnostics.Debug.WriteLine($"   - Roket İrtifa: {rocketData.RocketAltitude:F2}m");
                    }
                    
                    if (payloadData != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"   - Payload Koordinatları: {payloadData.PayloadLatitude:F6}, {payloadData.PayloadLongitude:F6}");
                        System.Diagnostics.Debug.WriteLine($"   - Payload İrtifa: {payloadData.PayloadAltitude:F2}m");
                    }

                    // Roket verileri - null kontrolü ile
                    if (rocketData != null)
                    {
                        RocketAltitudeText.Text = $"{rocketData.RocketAltitude:F1} m";
                        RocketGpsAltitudeText.Text = $"{rocketData.RocketGpsAltitude:F1} m";
                        RocketLatitudeText.Text = $"{rocketData.RocketLatitude:F6}";
                        RocketLongitudeText.Text = $"{rocketData.RocketLongitude:F6}";
                        RocketSpeedText.Text = $"{rocketData.RocketSpeed:F2} m/s";
                        RocketTemperatureText.Text = $"{rocketData.RocketTemperature:F1} °C";
                        RocketPressureText.Text = $"{rocketData.RocketPressure:F1} hPa";

                        // Jiroskop verileri
                        GyroXText.Text = $"{rocketData.GyroX:F2} °/s";
                        GyroYText.Text = $"{rocketData.GyroY:F2} °/s";
                        GyroZText.Text = $"{rocketData.GyroZ:F2} °/s";

                        // İvme verileri
                        AccelXText.Text = $"{rocketData.AccelX:F2} m/s²";
                        AccelYText.Text = $"{rocketData.AccelY:F2} m/s²";
                        AccelZText.Text = $"{rocketData.AccelZ:F2} m/s²";
                        AngleText.Text = $"{rocketData.Angle:F2}°";
                        
                        // ✅ KOORDİNAT DURUMU LOGLA
                        System.Diagnostics.Debug.WriteLine($"🌍 UI Roket koordinatları güncellendi: {rocketData.RocketLatitude:F6}, {rocketData.RocketLongitude:F6}");
                    }
                    
                    // Payload verileri
                    if (payloadData != null)
                    {
                        PayloadAltitudeText.Text = $"{payloadData.PayloadAltitude:F1} m";
                        PayloadGPSAltitudeText.Text = $"{payloadData.PayloadGpsAltitude:F1} m";
                        PayloadLatitudeText.Text = $"{payloadData.PayloadLatitude:F6}";
                        PayloadLongitudeText.Text = $"{payloadData.PayloadLongitude:F6}";
                        PayloadSpeedText.Text = $"{payloadData.PayloadSpeed:F2} m/s";
                        PayloadTemperatureText.Text = $"{payloadData.PayloadTemperature:F1} °C";
                        PayloadPressureText.Text = $"{payloadData.PayloadPressure:F1} hPa";
                        PayloadHumidityText.Text = $"{payloadData.PayloadHumidity:F1} %";
                        
                        // ✅ KOORDİNAT DURUMU LOGLA
                        System.Diagnostics.Debug.WriteLine($"🌍 UI Payload koordinatları güncellendi: {payloadData.PayloadLatitude:F6}, {payloadData.PayloadLongitude:F6}");
                    }

                    // ✅ GPS haritasını güncelle - HER DURUMDA ÇAĞIR!
                    double rLat = rocketData?.RocketLatitude ?? 0;
                    double rLon = rocketData?.RocketLongitude ?? 0; 
                    double rAlt = rocketData?.RocketAltitude ?? 0;
                    double pLat = payloadData?.PayloadLatitude ?? 0;
                    double pLon = payloadData?.PayloadLongitude ?? 0;
                    double pAlt = payloadData?.PayloadAltitude ?? 0;
                    
                    System.Diagnostics.Debug.WriteLine($"🗺️ GPS harita güncellemesi çağrılıyor...");
                    System.Diagnostics.Debug.WriteLine($"   - Roket: ({rLat:F6}, {rLon:F6}) Alt: {rAlt:F1}m");
                    System.Diagnostics.Debug.WriteLine($"   - Payload: ({pLat:F6}, {pLon:F6}) Alt: {pAlt:F1}m");
                    
                    UpdateSitPageGpsPositions(rLat, rLon, rAlt, pLat, pLon, pAlt);

                    // Chart'lara veri gönder - sadece veri varsa
                    if (rocketData != null || payloadData != null)
                    {
                        SendDataToCharts(rocketData, payloadData);
                        UpdateStatistics(rocketData, payloadData);
                    }

                    // Son güncelleme zamanı
                    LastUpdateText.Text = $"{DateTime.Now:HH:mm:ss}";

                    _Counter = (_Counter + 1) % 256;
                    DataCountText.Text = _Counter.ToString();

                    System.Diagnostics.Debug.WriteLine($"✅ sitPage telemetri ve GPS güncellendi - Roket İrtifa: {rocketData?.RocketAltitude:F1}m, Payload İrtifa: {payloadData?.PayloadAltitude:F1}m");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ sitPage telemetri güncelleme hatası: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"   - Stack Trace: {ex.StackTrace}");
                    LastUpdateText.Text = $"Hata: {DateTime.Now:HH:mm:ss}";
                }
            });
        }

        private void SendDataToCharts(RocketTelemetryData rocketData, PayloadTelemetryData payloadData)
        {
            try
            {
                // ViewModel varsa ve GERÇEKTENç VERİ VARSA güncelle
                if (_viewModel != null)
                {
                    // SADECE ROKET VERİSİ VAR VE GERÇEKSİ
                    if (rocketData != null)
                    {
                        _viewModel.AddRocketAltitudeValue(rocketData.RocketAltitude);
                        _viewModel.addRocketAccelXValue(rocketData.AccelX);
                        _viewModel.addRocketAccelYValue(rocketData.AccelY);
                        _viewModel.addRocketAccelZValue(rocketData.AccelZ);
                        _viewModel.addRocketSpeedValue(rocketData.RocketSpeed);
                        _viewModel.addRocketTempValue(rocketData.RocketTemperature);
                        _viewModel.addRocketPressureValue(rocketData.RocketPressure);
                    }
                    
                    // SADECE PAYLOAD VERİSİ VARSA VE GERÇEKSİ (dummy değilse)
                    if (payloadData != null && payloadData.PacketCounter > 0) // Gerçek payload verisi kontrolü
                    {
                        _viewModel.addPayloadAltitudeValue(payloadData.PayloadAltitude);
                        _viewModel.addPayloadSpeedValue(payloadData.PayloadSpeed);
                        _viewModel.addPayloadTempValue(payloadData.PayloadTemperature);
                        _viewModel.addPayloadPressureValue(payloadData.PayloadPressure);
                        _viewModel.addPayloadHumidityValue(payloadData.PayloadHumidity);
                        
                        System.Diagnostics.Debug.WriteLine("sitPage: GERÇEİ PAYLOAD VERİSİ chart'a eklendi");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("sitPage: Payload dummy verisi - chart'a eklenmedi");
                    }
                    
                    System.Diagnostics.Debug.WriteLine("sitPage chart verileri ViewModel'e gönderildi");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"sitPage chart güncelleme hatası: {ex.Message}");
            }
        }

        private void UpdateStatistics(RocketTelemetryData rocketData, PayloadTelemetryData payloadData)
        {
            try
            {
                // Maksimum irtifa hesapla
                float rocketAlt = rocketData?.RocketAltitude ?? 0;
                float payloadAlt = payloadData?.PayloadAltitude ?? 0;
                float currentMaxAltitude = Math.Max(rocketAlt, payloadAlt);
                
                if (currentMaxAltitude > _maxAltitude)
                {
                    _maxAltitude = currentMaxAltitude;
                    MaxAltitudeText.Text = $"{_maxAltitude:F1} m";
                }

                // CRC değerini güncelle - önce roket sonra payload
                if (rocketData?.CRC >= 0) 
                {
                    _CRC = rocketData.CRC;
                    CRCText.Text = _CRC.ToString();
                }
                else if (payloadData?.CRC >= 0)
                {
                    _CRC = payloadData.CRC;
                    CRCText.Text = _CRC.ToString();
                }

                // Team ID değerini güncelle
                if (rocketData?.TeamID > 0) 
                {
                    _TeamID = rocketData.TeamID;
                    TeamIDText.Text = _TeamID.ToString();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"İstatistik güncelleme hatası: {ex.Message}");
            }
        }

        private void ConnectToSerialPortService()
        {
            try
            {
                // ✨ YENİ: Önce GlobalPortManager'dan port durumunu kontrol et
                var inputPortStatus = GlobalPortManager.GetInputPortStatus();
                if (inputPortStatus != null && inputPortStatus.IsConnected)
                {
                    // GlobalPortManager'dan SerialPortService instance'ını al
                    _serialPortService = inputPortStatus.ServiceInstance;
                    
                    if (_serialPortService != null)
                    {
                        // Event handler'ları bağla
                        _serialPortService.OnTelemetryDataUpdated += OnTelemetryDataUpdated;
                        _serialPortService.OnDataReceived += OnSerialDataReceived;
                        _serialPortService.OnRotationDataReceived += OnRotationDataReceived;
                        
                        // ✨ YENİ: Sadece roket verilerini debug için dinle
                        _serialPortService.OnRocketDataUpdated += OnRocketDataReceivedDebug;
                        
                        // ViewModel'i al
                        _viewModel = _serialPortService.ViewModel ?? new ChartViewModel();
                        
                        // ✅ Port bağlantı bilgisini göster
                        System.Diagnostics.Debug.WriteLine($"✅ sitPage GlobalPortManager'dan SerialPortService'e bağlandı: {inputPortStatus.PortName}");
                        System.Diagnostics.Debug.WriteLine($"📅 Bağlantı zamanı: {inputPortStatus.ConnectedAt:HH:mm:ss}");
                        System.Diagnostics.Debug.WriteLine($"🔧 Port türü: {inputPortStatus.Type}");
                        
                        return;
                    }
                }

                // ✨ YENİ: GlobalPortManager'da port yoksa eski yöntemi dene
                System.Diagnostics.Debug.WriteLine("⚠️ sitPage: GlobalPortManager'da aktif input port bulunamadı, eski yöntemi deneniyor...");
                
                // SettingPage'den SerialPortService instance'ını al (backward compatibility)
                _serialPortService = SettingPage.GetInputSerialPortService();
                
                if (_serialPortService != null)
                {
                    // Event handler'ları bağla
                    _serialPortService.OnTelemetryDataUpdated += OnTelemetryDataUpdated;
                    _serialPortService.OnDataReceived += OnSerialDataReceived;
                    _serialPortService.OnRotationDataReceived += OnRotationDataReceived;
                    
                    // ✨ YENİ: Sadece roket verilerini debug için dinle
                    _serialPortService.OnRocketDataUpdated += OnRocketDataReceivedDebug;
                    
                    // ViewModel'i al
                    _viewModel = _serialPortService.ViewModel ?? new ChartViewModel();
                    
                    System.Diagnostics.Debug.WriteLine("sitPage SerialPortService'e bağlandı (eski yöntem)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ sitPage: SerialPortService instance bulunamadı! Port bağlantısı yapılmamış olabilir.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"sitPage SerialPortService bağlantı hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// 🚀 SADECE ROKET VERİLERİNİ DEBUG KISMINDA YAZDIRAN METOD
        /// Roket telemetri verilerini aynı sıra ile debug output'a yazdırır
        /// </summary>
        private void OnRocketDataReceivedDebug(RocketTelemetryData rocketData)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🚀 =============== ROKET TELEMETRİ VERİSİ ===============");
                System.Diagnostics.Debug.WriteLine($"📦 PAKET SAYACI: {rocketData.PacketCounter}");

                // Gerçek veri kontrolü
                if (rocketData.PacketCounter > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"   - Gerçek Roket İrtifası: {rocketData.RocketAltitude:F1} m");
                    System.Diagnostics.Debug.WriteLine($"   - Gerçek Roket Koordinatları: {rocketData.RocketLatitude:F6}, {rocketData.RocketLongitude:F6}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"   - Dummy Roket Verisi (güncellenmedi)");
                }
                
                System.Diagnostics.Debug.WriteLine("🚀 ==================================================");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🚀 Hata ayıklama hatası: {ex.Message}");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // Event handler'ları kaldır
            if (_serialPortService != null)
            {
                _serialPortService.OnTelemetryDataUpdated -= OnTelemetryDataUpdated;
                _serialPortService.OnDataReceived -= OnSerialDataReceived;
                _serialPortService.OnRotationDataReceived -= OnRotationDataReceived;
                
                // ✨ YENİ: Roket debug event handler'ını da kaldır
                _serialPortService.OnRocketDataUpdated -= OnRocketDataReceivedDebug;
            }
            
            System.Diagnostics.Debug.WriteLine("sitPage'den ayrıldı - Event handler'lar kaldırıldı");
        }
    }
}
