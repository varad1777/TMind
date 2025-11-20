using System;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MyApp.Infrastructure.Services
{
    /// <summary>
    /// Minimal Modbus TCP helper implementing Read Holding Registers (function 3).
    /// Avoids third-party library and works with basic Modbus/TCP servers.
    /// NOTE: This is intentionally small and only implements what the poller needs.
    /// </summary>
    public static class ModbusTcpClient
    {
        private static int _txId = 0;

        public static async Task<ushort[]> ReadHoldingRegistersAsync(TcpClient tcp, byte unitId, ushort startAddress, ushort quantity, CancellationToken ct)
        {
            if (tcp == null || !tcp.Connected) throw new InvalidOperationException("TcpClient must be connected");

            var stream = tcp.GetStream();

            // Build MBAP header + PDU
            // MBAP: TransactionId(2), ProtocolId(2=0), Length(2), UnitId(1)
            // PDU: Function(1=3), StartAddr(2), Quantity(2)

            ushort tx = unchecked((ushort)System.Threading.Interlocked.Increment(ref _txId));
            byte[] req = new byte[12];
            var reqSpan = req.AsSpan();
            BinaryPrimitives.WriteUInt16BigEndian(reqSpan.Slice(0, 2), tx); // transaction id
            BinaryPrimitives.WriteUInt16BigEndian(reqSpan.Slice(2, 2), 0); // protocol id
            BinaryPrimitives.WriteUInt16BigEndian(reqSpan.Slice(4, 2), 6); // length = remaining bytes (unitid + pdu)
            req[6] = unitId; // unit id
            req[7] = 3; // function code Read Holding Registers
            BinaryPrimitives.WriteUInt16BigEndian(reqSpan.Slice(8, 2), startAddress);
            BinaryPrimitives.WriteUInt16BigEndian(reqSpan.Slice(10, 2), quantity);

            await stream.WriteAsync(req, 0, req.Length, ct).ConfigureAwait(false);

            // Read MBAP response header (7 bytes)
            byte[] header = new byte[7];
            int got = 0;
            while (got < 7)
            {
                int n = await stream.ReadAsync(header, got, 7 - got, ct).ConfigureAwait(false);
                if (n == 0) throw new SocketException((int)System.Net.Sockets.SocketError.ConnectionReset);
                got += n;
            }

            // Validate Transaction id
            ushort respTx = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
            ushort proto = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
            ushort len = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
            byte respUnit = header[6];

            if (respTx != tx) throw new InvalidOperationException("Transaction id mismatch in Modbus response");
            if (proto != 0) throw new InvalidOperationException("Unsupported Modbus protocol id");
            if (respUnit != unitId) { /* not fatal - some servers echo 0 */ }

            // Now PDU: function code (1), byte count (1), data...
            // len includes unit id + pdu length. We've already consumed unit id in header length count.
            int pduLen = len - 1; // minus unit id
            if (pduLen < 2) throw new InvalidOperationException("Invalid PDU length");

            byte[] pdu = new byte[pduLen];
            got = 0;
            while (got < pduLen)
            {
                int n = await stream.ReadAsync(pdu, got, pduLen - got, ct).ConfigureAwait(false);
                if (n == 0) throw new SocketException((int)System.Net.Sockets.SocketError.ConnectionReset);
                got += n;
            }

            byte func = pdu[0];
            if ((func & 0x80) != 0)
            {
                // exception response
                byte exCode = pdu.Length >= 2 ? pdu[1] : (byte)0;
                throw new InvalidOperationException($"Modbus slave exception: code {exCode}");
            }

            byte byteCount = pdu[1];
            if (byteCount != pduLen - 2) ; // tolerate mismatch

            int regCount = byteCount / 2;
            var regs = new ushort[regCount];
            for (int i = 0; i < regCount; i++)
            {
                int off = 2 + i * 2;
                regs[i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(off, 2));
            }

            return regs;
        }
    }
}
