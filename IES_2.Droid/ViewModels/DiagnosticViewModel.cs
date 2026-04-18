using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IES_2.ECU;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace IES_2.Droid
{
    public partial class DiagnosticViewModel : ObservableObject
    {
        // ── BLE ───────────────────────────────────────────────────────────────────
        [ObservableProperty] private bool isScanning;
        [ObservableProperty] private bool isConnected;
        [ObservableProperty] private string statusMessage = "Pronto";
        [ObservableProperty] private string ecuInfo = "";
        [ObservableProperty] private string selectedParameterName;

        public ObservableCollection<IDevice> BleDevices { get; } = new();
        public ObservableCollection<ParameterItem> Parameters { get; } = new();
        public ObservableCollection<string> ParameterNames { get; } = new();

        // ── LiveCharts 2 ──────────────────────────────────────────────────────────
        private readonly ObservableCollection<ObservablePoint> _chartPoints = new();
        public ISeries[] ChartSeries { get; }
        public Axis[] XAxes { get; } = new[] { new Axis { Name = "Tempo (s)", Labeler = v => $"{v:0.0}s" } };
        public Axis[] YAxes { get; } = new[] { new Axis { Name = "" } };
        private int _selectedParamIndex = -1;

        // ── ECU / transport ───────────────────────────────────────────────────────
        private ISerialTransport _transport;
        private ecu _ecu;
        private CancellationTokenSource _diagCts;
        private readonly Stopwatch _stopwatch = new();

        private readonly IBluetoothLE _ble;
        private readonly IAdapter _adapter;

        public DiagnosticViewModel()
        {
            _ble     = CrossBluetoothLE.Current;
            _adapter = CrossBluetoothLE.Current.Adapter;
            _adapter.ScanTimeout = 10000;
            _adapter.DeviceDiscovered += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Device.Name) &&
                        !BleDevices.Any(d => d.Id == e.Device.Id))
                        BleDevices.Add(e.Device);
                });
            };

            ChartSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values       = _chartPoints,
                    Stroke       = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2 },
                    Fill         = null,
                    GeometrySize = 0,
                    Name         = "—"
                }
            };
        }

        // ── Commands ──────────────────────────────────────────────────────────────

        [RelayCommand]
        public async Task ScanBleAsync()
        {
            if (IsScanning)
            {
                await _adapter.StopScanningForDevicesAsync();
                IsScanning = false;
                return;
            }
            BleDevices.Clear();
            IsScanning     = true;
            StatusMessage  = "Scansione BLE in corso…";
            try { await _adapter.StartScanningForDevicesAsync(); }
            finally
            {
                IsScanning    = false;
                StatusMessage = BleDevices.Count == 0
                    ? "Nessun dispositivo trovato"
                    : $"Trovati {BleDevices.Count} dispositivo/i";
            }
        }

        [RelayCommand]
        public async Task ConnectAsync(IDevice device)
        {
            if (IsConnected) return;
            StatusMessage = $"Connessione a {device.Name}…";
            try
            {
                await _adapter.ConnectToDeviceAsync(device);
                var transport = new AndroidBleTransport(device);
                transport.Open();
                _transport   = transport;
                IsConnected  = true;
                StatusMessage = $"Connesso a {device.Name}";
                await InitEcuAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Errore connessione: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task DisconnectAsync()
        {
            _diagCts?.Cancel();
            _transport?.Close();
            _transport   = null;
            _ecu         = null;
            IsConnected  = false;
            StatusMessage = "Disconnesso";
            EcuInfo      = "";
            Parameters.Clear();
            ParameterNames.Clear();
            _chartPoints.Clear();
            await Task.CompletedTask;
        }

        public async Task RequestBlePermissionsAsync()
        {
            var status = await Permissions.RequestAsync<Permissions.Bluetooth>();
            if (status != PermissionStatus.Granted)
                StatusMessage = "Permessi Bluetooth non concessi";
        }

        // ── ECU initialisation ────────────────────────────────────────────────────

        private async Task InitEcuAsync()
        {
            StatusMessage = "Inizializzazione ECU…";
            await Task.Run(() =>
            {
                try
                {
                    bool ok = ecu.InitPasvDiag(_transport);
                    if (!ok) { StatusMessage = "Inizializzazione fallita"; return; }

                    _ecu = DetectEcu();
                    if (_ecu == null) { StatusMessage = "Tipo ECU non riconosciuto"; return; }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        EcuInfo       = $"{_ecu.GetCarModel()} [{ecu.ISO}]";
                        StatusMessage = "ECU connessa – lettura dati in corso";
                        FillParameters();
                        StartDiagLoop();
                    });
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(() => StatusMessage = $"Errore ECU: {ex.Message}");
                }
            });
        }

        private ecu DetectEcu()
        {
            // Read ISO code once
            ecu.ReadISO(_transport);

            // Candidate ECU types in priority order
            var candidates = new (string typeName, Func<bool> checkISO, Func<bool> checkCODRIC)[]
            {
                ("iaw16f",  () => IES_2.ECU.iaw16f.CheckISO(),  null),
                ("iaw18f",  () => IES_2.ECU.iaw18f.CheckISO(),  null),
                ("iaw18fd", () => IES_2.ECU.iaw18fd.CheckISO(), null),
                ("iaw8f_68",() => IES_2.ECU.iaw8f_68.CheckISO(), null),
                ("iaw04k",  () => IES_2.ECU.iaw04k.CheckISO(),  null),
                ("code",    () => ecu.CheckCODE(_transport),     null),
            };

            foreach (var (typeName, checkISO, _) in candidates)
            {
                if (!checkISO()) continue;
                return typeName switch
                {
                    "iaw16f"   => new iaw16f(_transport),
                    "iaw18f"   => new iaw18f(_transport),
                    "iaw18fd"  => new iaw18fd(_transport),
                    "iaw8f_68" => new iaw8f_68(_transport),
                    "iaw04k"   => new iaw04k(_transport),
                    "code"     => new code(_transport),
                    _          => null
                };
            }
            return null;
        }

        private void FillParameters()
        {
            Parameters.Clear();
            ParameterNames.Clear();
            if (_ecu == null) return;
            foreach (var d in _ecu.engineData)
            {
                Parameters.Add(new ParameterItem { Name = d.Description, Value = "", Unit = d.Unit });
                ParameterNames.Add(d.Description);
            }
        }

        // ── Diagnostic loop ───────────────────────────────────────────────────────

        private void StartDiagLoop()
        {
            _diagCts = new CancellationTokenSource();
            var ct   = _diagCts.Token;
            _stopwatch.Restart();

            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested && IsConnected)
                {
                    try
                    {
                        foreach (var d in _ecu.engineData)
                        {
                            if (ct.IsCancellationRequested) break;
                            if (ecu.Query(_transport, d.RequestSet[0], out byte resp))
                                ecu.Buffer[d.RequestSet[0]] = resp;
                        }

                        double t = _stopwatch.ElapsedMilliseconds / 1000.0;

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            for (int i = 0; i < _ecu.engineData.Length && i < Parameters.Count; i++)
                                Parameters[i].Value = _ecu.engineData[i].FormattedValue;

                            // Update chart with selected parameter
                            if (_selectedParamIndex >= 0 && _selectedParamIndex < _ecu.engineData.Length)
                            {
                                double val = (double)_ecu.engineData[_selectedParamIndex].Value;
                                _chartPoints.Add(new ObservablePoint(t, val));
                                if (_chartPoints.Count > 200)
                                    _chartPoints.RemoveAt(0);
                            }
                        });

                        await Task.Delay(250, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        MainThread.BeginInvokeOnMainThread(() => StatusMessage = $"Errore lettura: {ex.Message}");
                        await Task.Delay(500, ct).ConfigureAwait(false);
                    }
                }
            }, ct);
        }

        partial void OnSelectedParameterNameChanged(string value)
        {
            _selectedParamIndex = ParameterNames.IndexOf(value ?? string.Empty);
            _chartPoints.Clear();
            if (ChartSeries.Length > 0)
                ChartSeries[0].Name = value ?? "—";
            XAxes[0].Name = "Tempo (s)";
        }

        // ── Nested types ──────────────────────────────────────────────────────────

        public class ParameterItem : ObservableObject
        {
            private string _value;
            public string Name  { get; set; }
            public string Unit  { get; set; }
            public string Value { get => _value; set => SetProperty(ref _value, value); }
        }
    }
}
