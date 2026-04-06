using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace IES_2
{
    /// <summary>
    /// <see cref="ISerialTransport"/> implementation that communicates over a
    /// Bluetooth LE UART-transparent service (Nordic NUS or the common 0xFFF0 variant).
    /// </summary>
    internal sealed class BleSerialTransport : ISerialTransport
    {
        // Nordic UART Service (NUS)
        private static readonly Guid NUS_SERVICE = new Guid("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
        private static readonly Guid NUS_TX      = new Guid("6E400002-B5A3-F393-E0A9-E50E24DCCA9E"); // app → device (Write)
        private static readonly Guid NUS_RX      = new Guid("6E400003-B5A3-F393-E0A9-E50E24DCCA9E"); // device → app (Notify)

        // Generic BLE UART (common in Chinese BLE OBD-II adapters)
        private static readonly Guid UART_SERVICE = new Guid("0000FFF0-0000-1000-8000-00805F9B34FB");
        private static readonly Guid UART_TX      = new Guid("0000FFF1-0000-1000-8000-00805F9B34FB"); // Write
        private static readonly Guid UART_RX      = new Guid("0000FFF2-0000-1000-8000-00805F9B34FB"); // Notify

        private readonly ulong bluetoothAddress;
        private BluetoothLEDevice device;
        private GattCharacteristic txChar;
        private GattCharacteristic rxChar;

        private readonly ConcurrentQueue<byte> rxQueue  = new ConcurrentQueue<byte>();
        private readonly SemaphoreSlim          dataReady = new SemaphoreSlim(0);

        private volatile bool isOpen;

        public bool   IsOpen      => isOpen;
        public int    BaudRate    { get; set; }   // Ignored – the BLE adapter handles the physical baud rate
        public int    ReadTimeout { get; set; } = 2000;
        public int    BytesToRead => rxQueue.Count;
        public string PortName    { get; set; }

        public BleSerialTransport(ulong bluetoothAddress, string deviceName)
        {
            this.bluetoothAddress = bluetoothAddress;
            PortName = deviceName;
        }

        // -------------------------------------------------------------------------
        // ISerialTransport – Open / Close
        // -------------------------------------------------------------------------

        public void Open()
        {
            if (isOpen) return;
            // Run the async connection logic on a thread-pool thread to avoid
            // deadlocking the UI SynchronizationContext.
            Task.Run(() => OpenAsync()).GetAwaiter().GetResult();
        }

        private async Task OpenAsync()
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask().ConfigureAwait(false);
            if (device == null)
                throw new IOException($"Impossibile connettersi al dispositivo BLE: {PortName}");

            GattCharacteristic foundTx = null, foundRx = null;

            // --- Try Nordic NUS service ---
            var nusSvcResult = await device.GetGattServicesForUuidAsync(NUS_SERVICE, BluetoothCacheMode.Uncached).AsTask().ConfigureAwait(false);
            if (nusSvcResult.Status == GattCommunicationStatus.Success && nusSvcResult.Services.Count > 0)
            {
                var svc = nusSvcResult.Services[0];
                foundTx = await GetCharacteristicAsync(svc, NUS_TX).ConfigureAwait(false);
                foundRx = await GetCharacteristicAsync(svc, NUS_RX).ConfigureAwait(false);
            }

            // --- Fall back to 0xFFF0-based service ---
            if (foundTx == null || foundRx == null)
            {
                var uartSvcResult = await device.GetGattServicesForUuidAsync(UART_SERVICE, BluetoothCacheMode.Uncached).AsTask().ConfigureAwait(false);
                if (uartSvcResult.Status == GattCommunicationStatus.Success && uartSvcResult.Services.Count > 0)
                {
                    var svc = uartSvcResult.Services[0];
                    foundTx = await GetCharacteristicAsync(svc, UART_TX).ConfigureAwait(false);
                    foundRx = await GetCharacteristicAsync(svc, UART_RX).ConfigureAwait(false);
                }
            }

            if (foundTx == null || foundRx == null)
                throw new IOException(
                    $"Servizio UART BLE non trovato su \"{PortName}\".\n" +
                    "Verificare che il dispositivo supporti il Nordic NUS (6E400001) o il servizio 0xFFF0.");

            // Subscribe to incoming notifications
            var cccdResult = await foundRx.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask().ConfigureAwait(false);
            if (cccdResult != GattCommunicationStatus.Success)
                throw new IOException("Impossibile attivare le notifiche BLE (CCCD write failed).");

            foundRx.ValueChanged += OnRxValueChanged;
            txChar = foundTx;
            rxChar  = foundRx;
            isOpen  = true;
        }

        private static async Task<GattCharacteristic> GetCharacteristicAsync(GattDeviceService service, Guid uuid)
        {
            var result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached).AsTask().ConfigureAwait(false);
            if (result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0)
                return result.Characteristics[0];
            return null;
        }

        public void Close()
        {
            isOpen = false;
            if (rxChar != null)
            {
                rxChar.ValueChanged -= OnRxValueChanged;
                rxChar = null;
            }
            txChar = null;
            device?.Dispose();
            device = null;
            DiscardInBuffer();
        }

        // -------------------------------------------------------------------------
        // ISerialTransport – Read / Write
        // -------------------------------------------------------------------------

        public void Write(byte[] buffer, int offset, int count)
        {
            if (!isOpen) throw new InvalidOperationException("BLE non connesso.");
            Task.Run(() => WriteAsync(buffer, offset, count)).GetAwaiter().GetResult();
        }

        private async Task WriteAsync(byte[] buffer, int offset, int count)
        {
            var writer = new DataWriter();
            byte[] payload = new byte[count];
            Array.Copy(buffer, offset, payload, 0, count);
            writer.WriteBytes(payload);
            IBuffer ibuffer = writer.DetachBuffer();
            var result = await txChar.WriteValueAsync(ibuffer, GattWriteOption.WriteWithoutResponse).AsTask().ConfigureAwait(false);
            if (result != GattCommunicationStatus.Success)
                throw new IOException("Errore durante la scrittura BLE.");
        }

        public int ReadByte()
        {
            if (!isOpen) throw new InvalidOperationException("BLE non connesso.");
            int timeout = ReadTimeout > 0 ? ReadTimeout : Timeout.Infinite;
            if (!dataReady.Wait(timeout))
                throw new IOException("Timeout lettura BLE.");
            if (rxQueue.TryDequeue(out byte b))
                return b;
            throw new IOException("Buffer di ricezione BLE vuoto.");
        }

        public void DiscardInBuffer()
        {
            while (rxQueue.TryDequeue(out _)) { }
            while (dataReady.Wait(0)) { }
        }

        // -------------------------------------------------------------------------
        // BLE notification handler
        // -------------------------------------------------------------------------

        private void OnRxValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            int count  = (int)reader.UnconsumedBufferLength;
            for (int i = 0; i < count; i++)
            {
                rxQueue.Enqueue(reader.ReadByte());
                dataReady.Release();
            }
        }
    }
}
