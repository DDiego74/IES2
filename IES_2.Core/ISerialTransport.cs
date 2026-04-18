using System;

namespace IES_2
{
    /// <summary>
    /// Abstraction over a byte-stream serial transport (COM port or Bluetooth LE UART).
    /// </summary>
    public interface ISerialTransport
    {
        /// <summary>Gets whether the transport is currently open/connected.</summary>
        bool IsOpen { get; }

        /// <summary>Baud rate (ignored by BLE transports).</summary>
        int BaudRate { get; set; }

        /// <summary>Read timeout in milliseconds (-1 for infinite).</summary>
        int ReadTimeout { get; set; }

        /// <summary>Number of bytes currently available in the receive buffer.</summary>
        int BytesToRead { get; }

        /// <summary>Transport identifier string (COM port name or BLE device name).</summary>
        string PortName { get; set; }

        /// <summary>Open/connect the transport.</summary>
        void Open();

        /// <summary>Close/disconnect the transport.</summary>
        void Close();

        /// <summary>Write <paramref name="count"/> bytes from <paramref name="buffer"/> starting at <paramref name="offset"/>.</summary>
        void Write(byte[] buffer, int offset, int count);

        /// <summary>
        /// Read one byte, blocking until data is available or <see cref="ReadTimeout"/> elapses.
        /// Throws <see cref="System.IO.IOException"/> on timeout.
        /// </summary>
        int ReadByte();

        /// <summary>Discard all data currently in the receive buffer.</summary>
        void DiscardInBuffer();
    }
}
