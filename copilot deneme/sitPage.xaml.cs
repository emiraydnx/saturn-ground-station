using copilot_deneme.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

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
        private double _currentRocketLat = 39.925533;  // Ankara başlangıç konumu
        private double _currentRocketLon = 32.866287;
        private double _currentPayloadLat = 39.925533;
        private double _currentPayloadLon = 32.866287;

        // Performans optimizasyonu için 3D model güncelleme throttling
        private float _lastYaw, _lastPitch, _lastRoll;
        private float _pendingYaw, _pendingPitch, _pendingRoll;
        private bool _hasNewRotationData = false;
        private DateTime _lastRotationUpdate = DateTime.Now;
        private readonly TimeSpan _rotationThrottleInterval = TimeSpan.FromMilliseconds(33); // ~30 FPS

        public sitPage()
        {
            this.InitializeComponent();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _viewModel = new ChartViewModel();

            InitializeDisplay();
            InitializeThreeDWebView();
            InitializeGpsMap();
            
            LogDebug("sitPage başlatıldı - SerialPortService bağlantısı bekleniyor");
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
                LogDebug("3D WebView başarıyla başlatıldı");
            }
            catch (Exception ex)
            {
                LogDebug($"3D WebView başlatma hatası: {ex.Message}");
            }
        }

        private void OnRotationDataReceived(float yaw, float pitch, float roll)
        {
            // Yeni veriyi pending olarak sakla
            _pendingYaw = yaw;
            _pendingPitch = pitch;
            _pendingRoll = roll;
            _hasNewRotationData = true;

            // Throttling: Son güncellemeden beri yeterli zaman geçtiyse güncelle
            var now = DateTime.Now;
            if (now - _lastRotationUpdate >= _rotationThrottleInterval)
            {
                // Değerler gerçekten değiştiyse güncelle
                if (_pendingYaw != _lastYaw || _pendingPitch != _lastPitch || _pendingRoll != _lastRoll)
                {
                    _lastYaw = _pendingYaw;
                    _lastPitch = _pendingPitch;
                    _lastRoll = _pendingRoll;
                    _lastRotationUpdate = now;

                    // Asenkron olarak 3D modeli güncelle
                    _ = UpdateRotationAsync(_lastYaw, _lastPitch, _lastRoll);
                }
                _hasNewRotationData = false;
            }
            // Eğer throttle süresi dolmadıysa, veri sadece pending olarak saklanır
        }

        // Telemetri verisi geldikçe bu metodu çağırın:
        public async Task UpdateRotationAsync(float yaw, float pitch, float roll)
        {
            try
            {
                if (ThreeDWebView.CoreWebView2 == null)
                    return;

                // JS fonksiyonunu çağır
                var script = $"updateModelRotation({yaw.ToString(CultureInfo.InvariantCulture)},{pitch.ToString(CultureInfo.InvariantCulture)},{roll.ToString(CultureInfo.InvariantCulture)});";
                await ThreeDWebView.ExecuteScriptAsync(script);
                
                LogDebug($"3D model güncellendi - Yaw: {yaw:F2}, Pitch: {pitch:F2}, Roll: {roll:F2}");
            }
            catch (Exception ex)
            {
                LogDebug($"3D model rotation güncelleme hatası: {ex.Message}");
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
                LogDebug("sitPage GPS harita başarıyla başlatıldı");
            }
            catch (Exception ex)
            {
                LogDebug($"sitPage GPS harita başlatma hatası: {ex.Message}");
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
        .info-panel {{
            position: absolute;
            top: 15px;
            right: 15px;
            background: rgba(0,0,0,0.85);
            color: white;
            padding: 12px 16px;
            border-radius: 8px;
            font-size: 13px;
            z-index: 1000;
            min-width: 200px;
            box-shadow: 0 4px 8px rgba(0,0,0,0.3);
        }}
        .info-title {{
            font-weight: bold;
            font-size: 14px;
            margin-bottom: 8px;
            color: #64B5F6;
        }}
        .coord-row {{
            margin: 4px 0;
            display: flex;
            justify-content: space-between;
        }}
        .altitude-panel {{
            position: absolute;
            top: 15px;
            left: 15px;
            background: rgba(0,0,0,0.85);
            color: white;
            padding: 12px 16px;
            border-radius: 8px;
            font-size: 12px;
            z-index: 1000;
            min-width: 160px;
            box-shadow: 0 4px 8px rgba(0,0,0,0.3);
        }}
        .alt-title {{
            font-weight: bold;
            font-size: 13px;
            margin-bottom: 6px;
            color: #81C784;
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
        
        // C# tarafından çağrılacak JavaScript fonksiyonları
        window.updateRocketPosition = function(lat, lon, alt) {{
            if (lat !== 0 && lon !== 0) {{
                var newPos = [lat, lon];
                rocketMarker.setLatLng(newPos);
                rocketPath.addLatLng(newPos);
                
                // Koordinat bilgisini güncelle
                document.getElementById('rocket-coords').textContent = lat.toFixed(6) + ', ' + lon.toFixed(6);
                document.getElementById('rocket-alt').textContent = alt.toFixed(2) + ' m';
                updateLastUpdateTime();
                updateDistance();
                fitMapToBounds();
            }}
        }};
        
        window.updatePayloadPosition = function(lat, lon, alt) {{
            if (lat !== 0 && lon !== 0) {{
                var newPos = [lat, lon];
                payloadMarker.setLatLng(newPos);
                payloadPath.addLatLng(newPos);
                
                // Koordinat bilgisini güncelle
                document.getElementById('payload-coords').textContent = lat.toFixed(6) + ', ' + lon.toFixed(6);
                document.getElementById('payload-alt').textContent = alt.toFixed(2) + ' m';
                updateLastUpdateTime();
                updateDistance();
                fitMapToBounds();
            }}
        }};
        
        window.updateBothPositions = function(rocketLat, rocketLon, rocketAlt, payloadLat, payloadLon, payloadAlt) {{
            var updated = false;
            
            if (rocketLat !== 0 && rocketLon !== 0) {{
                var rocketPos = [rocketLat, rocketLon];
                rocketMarker.setLatLng(rocketPos);
                rocketPath.addLatLng(rocketPos);
                document.getElementById('rocket-coords').textContent = rocketLat.toFixed(6) + ', ' + rocketLon.toFixed(6);
                document.getElementById('rocket-alt').textContent = rocketAlt.toFixed(2) + ' m';
                updated = true;
            }}
            
            if (payloadLat !== 0 && payloadLon !== 0) {{
                var payloadPos = [payloadLat, payloadLon];
                payloadMarker.setLatLng(payloadPos);
                payloadPath.addLatLng(payloadPos);
                document.getElementById('payload-coords').textContent = payloadLat.toFixed(6) + ', ' + payloadLon.toFixed(6);
                document.getElementById('payload-alt').textContent = payloadAlt.toFixed(2) + ' m';
                updated = true;
            }}
            
            if (updated) {{
                updateLastUpdateTime();
                updateDistance();
                fitMapToBounds();
            }}
        }};
        
        function updateDistance() {{
            var rocketPos = rocketMarker.getLatLng();
            var payloadPos = payloadMarker.getLatLng();
            var distance = calculateDistance(rocketPos.lat, rocketPos.lng, payloadPos.lat, payloadPos.lng);
            document.getElementById('distance').textContent = distance.toFixed(2) + ' m';
        }}
        
        function fitMapToBounds() {{
            var group = new L.featureGroup([rocketMarker, payloadMarker]);
            var bounds = group.getBounds();
            if (bounds.isValid()) {{
                map.fitBounds(bounds.pad(0.15));
            }}
        }}
        
        function updateLastUpdateTime() {{
            var now = new Date();
            var timeStr = now.getHours().toString().padStart(2, '0') + ':' + 
                         now.getMinutes().toString().padStart(2, '0') + ':' + 
                         now.getSeconds().toString().padStart(2, '0');
            document.getElementById('last-update').textContent = timeStr;
        }}
        
        // İlk güncelleme
        updateLastUpdateTime();
        updateDistance();
    </script>
</body>
</html>";
        }

        private async void UpdateRocketGpsPosition(double rocketLat, double rocketLon, double rocketAlt)
        {
            if (!_isMapInitialized) return;
            
            try
            {
                // Sadece geçerli koordinatları güncelle
                if (rocketLat != 0 && rocketLon != 0)
                {
                    _currentRocketLat = rocketLat;
                    _currentRocketLon = rocketLon;
                    
                    // JavaScript fonksiyonunu çağır - string interpolation optimize edildi
                    var script = $"updateRocketPosition({_currentRocketLat.ToString(CultureInfo.InvariantCulture)},{_currentRocketLon.ToString(CultureInfo.InvariantCulture)},{rocketAlt.ToString(CultureInfo.InvariantCulture)})";
                    
                    await MapWebView.ExecuteScriptAsync(script);
                    LogDebugGpsRocket(_currentRocketLat, _currentRocketLon, rocketAlt);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"sitPage GPS pozisyon güncelleme hatası: {ex.Message}");
            }
        }

        private async void UpdatePayloadGpsPosition(double payloadLat, double payloadLon, double payloadAlt)
        {
            if (!_isMapInitialized) return;

            try
            {
                // Sadece geçerli koordinatları güncelle
                if (payloadLat != 0 && payloadLon != 0)
                {
                    _currentPayloadLat = payloadLat;
                    _currentPayloadLon = payloadLon;

                    // JavaScript fonksiyonunu çağır
                    var script = $"updatePayloadPosition({_currentPayloadLat.ToString(CultureInfo.InvariantCulture)},{_currentPayloadLon.ToString(CultureInfo.InvariantCulture)},{payloadAlt.ToString(CultureInfo.InvariantCulture)})";

                    await MapWebView.ExecuteScriptAsync(script);
                    LogDebugGpsPayload(_currentPayloadLat, _currentPayloadLon, payloadAlt);
                }
            }
            catch (Exception ex)
            {
                LogDebug($"sitPage Payload GPS pozisyon güncelleme hatası: {ex.Message}");
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
            // Bu sadece bağlantı durumunu göstermek için - optimize edildi
            _dispatcherQueue.TryEnqueue(() =>
            {
                LastUpdateText.Text = $"Veri alıyor: {DateTime.Now:HH:mm:ss}";
            });
        }

        private void OnTelemetryDataUpdated(SerialPortService.RocketTelemetryData rocketData)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Roket verileri - null kontrolü ile
                    if (rocketData != null)
                    {
                        // UI güncellemelerini batch olarak yap - performance artışı
                        BatchUpdateUI(rocketData);

                        // GPS haritasını güncelle
                        UpdateRocketGpsPosition(rocketData.RocketLatitude, rocketData.RocketLongitude, rocketData.RocketAltitude);

                        // Chart'lara veri gönder
                        SendDataToCharts(rocketData);

                        // İstatistikleri güncelle
                        UpdateStatistics(rocketData);
                    }

                    // Son güncelleme zamanı
                    LastUpdateText.Text = $"{DateTime.Now:HH:mm:ss}";

                    _Counter = (_Counter + 1) % 256;
                    DataCountText.Text = _Counter.ToString();

                    LogDebugTelemetry(rocketData?.RocketAltitude ?? 0);
                }
                catch (Exception ex)
                {
                    LogDebug($"sitPage telemetri güncelleme hatası: {ex.Message}");
                    LastUpdateText.Text = $"Hata: {DateTime.Now:HH:mm:ss}";
                }
            });
        }
        
        // UI güncellemelerini batch'le - performans artışı
        private void BatchUpdateUI(SerialPortService.RocketTelemetryData rocketData)
        {
            // Tüm UI güncellemelerini tek seferde yap
            RocketAltitudeText.Text = $"{rocketData.RocketAltitude:F1} m";
            RocketGpsAltitudeText.Text = $"{rocketData.RocketGpsAltitude:F1} m";
            RocketLatitudeText.Text = $"{rocketData.RocketLatitude:F6}";
            RocketLongitudeText.Text = $"{rocketData.RocketLongitude:F6}";
            RocketSpeedText.Text = $"{rocketData.RocketSpeed:F2} m/s";
            RocketTemperatureText.Text = $"{rocketData.RocketTemperature:F1} °C";
            RocketPressureText.Text = $"{rocketData.RocketPressure:F2} hPa";
            RocketPressureDerivativeText.Text = $"{rocketData.DpDt:F1} dP/dt";

            // Jiroskop verileri
            GyroXText.Text = $"{rocketData.GyroX:F2} °/s";
            GyroYText.Text = $"{rocketData.GyroY:F2} °/s";
            GyroZText.Text = $"{rocketData.GyroZ:F2} °/s";

            // İvme verileri
            AccelXText.Text = $"{rocketData.AccelX:F2} m/s²";
            AccelYText.Text = $"{rocketData.AccelY:F2} m/s²";
            AccelZText.Text = $"{rocketData.AccelZ:F2} m/s²";
            AngleText.Text = $"{rocketData.Angle:F2}°";
        }

        private void SendDataToCharts(SerialPortService.RocketTelemetryData rocketData)
        {
            try
            {
                // ViewModel varsa ve GERÇEKTEN VERİ VARSA güncelle
                if (_viewModel != null && rocketData != null)
                {
                    // Chart güncellemelerini optimize et
                    _viewModel.AddRocketAltitudeValue(rocketData.RocketAltitude);
                    _viewModel.addRocketAccelXValue(rocketData.AccelX);
                    _viewModel.addRocketAccelYValue(rocketData.AccelY);
                    _viewModel.addRocketAccelZValue(rocketData.AccelZ);
                    _viewModel.addRocketSpeedValue(rocketData.RocketSpeed);
                    _viewModel.addRocketTempValue(rocketData.RocketTemperature);
                    _viewModel.addRocketPressureValue(rocketData.RocketPressure);
                    
                    LogDebug("sitPage chart verileri ViewModel'e gönderildi");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"sitPage chart güncelleme hatası: {ex.Message}");
            }
        }

        private void UpdateStatistics(SerialPortService.RocketTelemetryData rocketData)
        {
            try
            {
                // Maksimum irtifa hesapla
                float rocketAlt = rocketData?.RocketAltitude ?? 0;
                
                if (rocketAlt > _maxAltitude)
                {
                    _maxAltitude = rocketAlt;
                    MaxAltitudeText.Text = $"{_maxAltitude:F1} m";
                }

                // CRC değerini güncelle
                if (rocketData?.CRC >= 0) 
                {
                    _CRC = rocketData.CRC;
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
                LogDebug($"İstatistik güncelleme hatası: {ex.Message}");
            }
        }

        private void ConnectToSerialPortService()
        {
            try
            {
                // SerialPortManager'dan SerialPortService instance'ını al
                _serialPortService = SerialPortManager.Instance.SerialPortService;
                
                if (_serialPortService != null)
                {
                    // ViewModel'i al
                    _viewModel = _serialPortService.ViewModel ?? new ChartViewModel();
                    
                    LogDebug("sitPage SerialPortManager'a bağlandı");
                }
                else
                {
                    LogDebug("sitPage: SerialPortService instance bulunamadı!");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"sitPage SerialPortManager bağlantı hatası: {ex.Message}");
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // SerialPortManager'a abone ol
            SerialPortManager.Instance.SubscribeToTelemetryData("sitPage", OnTelemetryDataUpdated);
            SerialPortManager.Instance.SubscribeToDataReceived("sitPage", OnSerialDataReceived);
            SerialPortManager.Instance.SubscribeToRotationData("sitPage", OnRotationDataReceived);
            
            // Payload verisi için abone ol
            SerialPortManager.Instance.SubscribeToPayloadData("sitPage", HandlePayloadData);
            
            // SerialPortService'e bağlan
            ConnectToSerialPortService();
            
            LogDebug("sitPage navigasyon tamamlandı - GPS harita sistemi hazır");
        }

        private void HandlePayloadData(SerialPortService.PayloadTelemetryData data)
        {
            // UI thread'inde olduğumuzdan emin olalım
            DispatcherQueue.TryEnqueue(() =>
            {
                // Textbox'ları güncelle
                PayloadAltitudeText.Text = data.Altitude.ToString("F2");
                PayloadGPSAltitudeText.Text = data.GpsAltitude.ToString("F2");
                PayloadLatitudeText.Text = data.Latitude.ToString("F6");
                PayloadLongitudeText.Text = data.Longitude.ToString("F6");
                PayloadTemperatureText.Text = data.Temperature.ToString("F2");
                PayloadHumidityText.Text = data.Humidity.ToString("F2");
                PayloadPressureText.Text = data.Pressure.ToString("F2");
                PayloadSpeedText.Text = data.PayloadSpeed.ToString("F2");

                // Harita kontrolünü güncelle
                UpdatePayloadGpsPosition(data.Latitude, data.Longitude, data.Altitude);
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // SerialPortManager'dan aboneliği iptal et
            SerialPortManager.Instance.UnsubscribeAll("sitPage");
            
            LogDebug("sitPage'den ayrıldı - Abonelikler iptal edildi");
        }
        
        // Performance Optimized Debug Methods - sadece DEBUG modunda çalışır
        [Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
        
        [Conditional("DEBUG")]
        private static void LogDebugTelemetry(float altitude)
        {
            System.Diagnostics.Debug.WriteLine($"sitPage telemetri ve GPS güncellendi - Roket İrtifa: {altitude:F1}m");
        }
        
        [Conditional("DEBUG")]
        private static void LogDebugGpsRocket(double lat, double lon, double alt)
        {
            System.Diagnostics.Debug.WriteLine($"sitPage GPS pozisyonu güncellendi - Roket: {lat:F6}, {lon:F6} ({alt:F1}m)");
        }

        [Conditional("DEBUG")]
        private static void LogDebugGpsPayload(double lat, double lon, double alt)
        {
            System.Diagnostics.Debug.WriteLine($"sitPage GPS pozisyonu güncellendi - Payload: {lat:F6}, {lon:F6} ({alt:F1}m)");
        }
    }
}
