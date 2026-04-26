using IES_2;
using Plugin.BLE.Abstractions.Contracts;
using System.Collections.Concurrent;

namespace IES_2.Droid
{
    /// <summary>
    /// ISerialTransport implementation for Android using Plugin.BLE.
    /// Supports Nordic NUS (6E400001) first, then generic 0xFFF0 UART service
    /// (used by most cheap OBD-II BLE dongles).
    /// </summary>
    internal sealed class AndroidBleTransport : ISerialTransport
    {
        private static readonly Guid NUS_SERVICE  = new Guid("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
        private static readonly Guid NUS_TX       = new Guid("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
        private static readonly Guid NUS_RX       = new Guid("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");
        private static readonly Guid UART_SERVICE = new Guid("0000FFF0-0000-1000-8000-00805F9B34FB");
        private static readonly Guid UART_TX      = new Guid("0000FFF1-0000-1000-8000-00805F9B34FB");
        private static readonly Guid UART_RX      = new Guid("0000FFF2-0000-1000-8000-00805F9B34FB");

        private readonly IDevice _device;
        private ICharacteristic _txChar;
        private ICharacteristic _rxChar;
        private readonly ConcurrentQueue<byte> _rxQueue  = new ConcurrentQueue<byte>();
        private readonly SemaphoreSlim          _dataReady = new SemaphoreSlim(0);
        private volatile bool _isOpen;

        public bool   IsOpen      => _isOpen;
        public int    BaudRate    { get; set; }   // ignored – physical baud is handled by adapter
        public int    ReadTimeout { get; set; } = 2000;
        public int    BytesToRead => _rxQueue.Count;
        public string PortName    { get; set; }

        public AndroidBleTransport(IDevice device)
        {
            _device  = device;
            PortName = device.Name ?? device.Id.ToString();
        }

        /// <summary>
        /// Synchronous wrapper kept for ISerialTransport compatibility.
        /// Prefer <see cref="OpenAsync"/> when calling from an async context
        /// to avoid blocking the calling thread.
        /// </summary>
        public void Open() => Task.Run(OpenAsync).GetAwaiter().GetResult();

        /// <summary>
        /// Async version of <see cref="Open"/>. Use this from async code paths
        /// to avoid deadlocking the Android main thread.
        /// </summary>
        public async Task OpenAsync()
        {
            AppLogger.Log($"BLE OpenAsync: discovering services on \"{PortName}\"");
            ICharacteristic foundTx = null, foundRx = null;

            // --- Try Nordic NUS ---
            AppLogger.Log("BLE OpenAsync: trying Nordic NUS service (6E400001)");
            var nusSvc = await _device.GetServiceAsync(NUS_SERVICE).ConfigureAwait(false);
            if (nusSvc != null)
            {
                AppLogger.Log("BLE OpenAsync: NUS service found, reading characteristics");
                foundTx = await nusSvc.GetCharacteristicAsync(NUS_TX).ConfigureAwait(false);
                foundRx = await nusSvc.GetCharacteristicAsync(NUS_RX).ConfigureAwait(false);
                AppLogger.Log($"BLE OpenAsync: NUS TX={foundTx != null}, RX={foundRx != null}");
            }
            else
            {
                AppLogger.Log("BLE OpenAsync: NUS service not found");
            }

            // --- Fallback to 0xFFF0 UART (common cheap dongles) ---
            if (foundTx == null || foundRx == null)
            {
                AppLogger.Log("BLE OpenAsync: trying UART 0xFFF0 service");
                var uartSvc = await _device.GetServiceAsync(UART_SERVICE).ConfigureAwait(false);
                if (uartSvc != null)
                {
                    AppLogger.Log("BLE OpenAsync: UART 0xFFF0 service found, reading characteristics");
                    foundTx = await uartSvc.GetCharacteristicAsync(UART_TX).ConfigureAwait(false);
                    foundRx = await uartSvc.GetCharacteristicAsync(UART_RX).ConfigureAwait(false);
                    AppLogger.Log($"BLE OpenAsync: UART TX={foundTx != null}, RX={foundRx != null}");
                }
                else
                {
                    AppLogger.Log("BLE OpenAsync: UART 0xFFF0 service not found");
                }
            }

            if (foundTx == null || foundRx == null)
            {
                string msg = $"Servizio UART BLE non trovato su \"{PortName}\". Verificare che il dispositivo supporti NUS (6E400001) o il servizio 0xFFF0.";
                AppLogger.LogError($"BLE OpenAsync: {msg}");
                throw new IOException(msg);
            }

            AppLogger.Log("BLE OpenAsync: subscribing to RX notifications");
            foundRx.ValueUpdated += OnRxValueUpdated;
            await foundRx.StartUpdatesAsync().ConfigureAwait(false);

            _txChar = foundTx;
            _rxChar = foundRx;
            _isOpen = true;
            AppLogger.Log($"BLE OpenAsync: transport open on \"{PortName}\"");
        }

        public void Close()
        {
            AppLogger.Log($"BLE Close: closing transport on \"{PortName}\"");
            _isOpen = false;
            if (_rxChar != null)
            {
                _rxChar.ValueUpdated -= OnRxValueUpdated;
                Task.Run(() => _rxChar.StopUpdatesAsync()).GetAwaiter().GetResult();
                _rxChar = null;
            }
            _txChar = null;
            DiscardInBuffer();
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (!_isOpen) throw new InvalidOperationException("BLE non connesso.");
            byte[] payload = new byte[count];
            Array.Copy(buffer, offset, payload, 0, count);
            _txChar.WriteType = Plugin.BLE.Abstractions.CharacteristicWriteType.WithoutResponse;
            Task.Run(() => _txChar.WriteAsync(payload)).GetAwaiter().GetResult();
        }

        public int ReadByte()
        {
            if (!_isOpen) throw new InvalidOperationException("BLE non connesso.");
            int timeout = ReadTimeout > 0 ? ReadTimeout : Timeout.Infinite;
            if (!_dataReady.Wait(timeout))
                throw new IOException("Timeout lettura BLE.");
            if (_rxQueue.TryDequeue(out byte b))
                return b;
            throw new IOException("Buffer di ricezione BLE vuoto.");
        }

        public void DiscardInBuffer()
        {
            while (_rxQueue.TryDequeue(out _)) { }
            while (_dataReady.Wait(0)) { }
        }

        private void OnRxValueUpdated(object sender, Plugin.BLE.Abstractions.EventArgs.CharacteristicUpdatedEventArgs e)
        {
            foreach (byte b in e.Characteristic.Value)
            {
                _rxQueue.Enqueue(b);
                _dataReady.Release();
            }
        }
    }
}
