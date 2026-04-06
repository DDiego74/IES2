using System.IO.Ports;

namespace IES_2
{
    /// <summary>
    /// <see cref="ISerialTransport"/> implementation that delegates to a
    /// <see cref="SerialPort"/> instance (classic COM / virtual COM port).
    /// </summary>
    internal sealed class SerialPortTransport : ISerialTransport
    {
        private readonly SerialPort port;

        public SerialPortTransport(SerialPort port)
        {
            this.port = port;
        }

        public bool IsOpen => port.IsOpen;
        public int BaudRate { get => port.BaudRate; set => port.BaudRate = value; }
        public int ReadTimeout { get => port.ReadTimeout; set => port.ReadTimeout = value; }
        public int BytesToRead => port.BytesToRead;
        public string PortName { get => port.PortName; set => port.PortName = value; }

        public void Open() => port.Open();
        public void Close() => port.Close();
        public void Write(byte[] buffer, int offset, int count) => port.Write(buffer, offset, count);
        public int ReadByte() => port.ReadByte();
        public void DiscardInBuffer() => port.DiscardInBuffer();
    }
}
