using System.Runtime.InteropServices;
using System.Text;

namespace FrubberPOS.Infrastructure.Scale;

/// <summary>
/// Raw Win32 serial connection for the scale (JHO-86, validado con la báscula
/// física BBG MARKET-30): System.IO.Ports reconfigures the port unconditionally
/// when opening, and the CH340 drivers used by USB scale cables commonly
/// reject that configuration with ERROR_GEN_FAILURE (31) even though the port
/// reads and writes fine. Opening raw and calling <c>SetCommState</c> only
/// when the live settings do not match keeps the flaky call out of the path:
/// CH340 chips boot at 9600 8N1 — exactly what these scales use — and any
/// other setting gets a short retry. No COM logic, no printing (RN-IMP-003).
/// </summary>
internal sealed class NativeSerialConnection : IDisposable
{
    private const uint GenericReadWrite = 0xC0000000;
    private const uint ShareReadWrite = 0x3;
    private const uint OpenExisting = 3;
    private const uint PurgeAll = 0x0F; // PURGE_TXCLEAR|PURGE_RXCLEAR (+aborts)

    private static readonly IntPtr InvalidHandle = new(-1);

    private readonly string portName;
    private readonly int baudRate;
    private readonly int readTimeoutMs;
    private IntPtr handle = InvalidHandle;

    public NativeSerialConnection(string portName, int baudRate, int readTimeoutMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentOutOfRangeException.ThrowIfLessThan(baudRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(readTimeoutMs, 1);
        this.portName = portName;
        this.baudRate = baudRate;
        this.readTimeoutMs = readTimeoutMs;
    }

    public void Open()
    {
        if (handle != InvalidHandle)
        {
            return;
        }

        handle = CreateFile(
            $@"\\.\{portName}",
            GenericReadWrite,
            ShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == InvalidHandle)
        {
            var error = Marshal.GetLastWin32Error();
            throw new IOException($"No se pudo abrir el puerto {portName} (error {error}).");
        }

        // Solo reconfiguramos si el estado vivo no coincide: llamar a
        // SetCommState sin necesidad es lo que los CH340 rechazan.
        var dcb = new Dcb { DCBlength = (uint)Marshal.SizeOf<Dcb>() };
        var configured = GetCommState(handle, ref dcb)
            && dcb.BaudRate == (uint)baudRate
            && dcb.ByteSize == 8
            && dcb.Parity == 0
            && dcb.StopBits == 0;
        if (!configured)
        {
            dcb.DCBlength = (uint)Marshal.SizeOf<Dcb>();
            dcb.BaudRate = (uint)baudRate;
            dcb.ByteSize = 8;
            dcb.Parity = 0;
            dcb.StopBits = 0;
            if (dcb.XonChar == 0)
            {
                dcb.XonChar = 0x11; // algunos drivers rechazan caracteres nulos
                dcb.XoffChar = 0x13;
            }

            var applied = false;
            for (var attempt = 0; attempt < 5 && !applied; attempt++)
            {
                applied = SetCommState(handle, ref dcb);
                if (!applied)
                {
                    Thread.Sleep(100);
                }
            }

            if (!applied)
            {
                var error = Marshal.GetLastWin32Error();
                throw new IOException($"No se pudo configurar {portName} a {baudRate} baudios (error {error}).");
            }
        }

        var timeouts = new CommTimeouts
        {
            ReadIntervalTimeout = 100,
            ReadTotalTimeoutMultiplier = 0,
            ReadTotalTimeoutConstant = (uint)readTimeoutMs,
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = (uint)readTimeoutMs,
        };
        if (!SetCommTimeouts(handle, ref timeouts))
        {
            throw new IOException($"No se pudo configurar los tiempos de espera de {portName}.");
        }
    }

    /// <summary>Drops any stale bytes (previous session's stream).</summary>
    public void Purge()
    {
        _ = PurgeComm(handle, PurgeAll);
    }

    public void Write(string command)
    {
        var bytes = Encoding.ASCII.GetBytes(command);
        if (!WriteFile(handle, bytes, bytes.Length, out _, IntPtr.Zero))
        {
            throw new IOException($"No se pudo escribir en {portName} (error {Marshal.GetLastWin32Error()}).");
        }
    }

    /// <summary>
    /// Reads one chunk; 0 means the timeout elapsed without data (the port
    /// is configured to return after a 100 ms gap or <c>readTimeoutMs</c>).
    /// </summary>
    public int ReadChunk(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length == 0)
        {
            return 0;
        }

        if (!ReadFile(handle, buffer, buffer.Length, out var read, IntPtr.Zero))
        {
            throw new IOException($"No se pudo leer de {portName} (error {Marshal.GetLastWin32Error()}).");
        }

        return read;
    }

    public void Dispose()
    {
        if (handle != InvalidHandle && handle != IntPtr.Zero)
        {
            _ = CloseHandle(handle);
            handle = InvalidHandle;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort WReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort WReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCommState(IntPtr file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommState(IntPtr file, ref Dcb dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(IntPtr file, ref CommTimeouts timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PurgeComm(IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr file, [Out] byte[] buffer, int count, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr file, byte[] buffer, int count, out int written, IntPtr overlapped);
}
