using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video;

/// <summary>Byte-level building blocks shared by container writers, not container policy.</summary>
internal static class ContainerWriterTools {

  internal static byte[] Build(Action<MemoryStream> write) {
    using var stream = new MemoryStream();
    write(stream);
    return stream.ToArray();
  }

  internal static void WriteAscii(Stream stream, string text) {
    Span<byte> bytes = stackalloc byte[Encoding.ASCII.GetMaxByteCount(text.Length)];
    var length = Encoding.ASCII.GetBytes(text, bytes);
    stream.Write(bytes[..length]);
  }

  internal static void WriteUInt16LittleEndian(Stream stream, ushort value) {
    Span<byte> bytes = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
    stream.Write(bytes);
  }

  internal static void WriteInt16BigEndian(Stream stream, short value) {
    Span<byte> bytes = stackalloc byte[2];
    BinaryPrimitives.WriteInt16BigEndian(bytes, value);
    stream.Write(bytes);
  }

  internal static void WriteUInt16BigEndian(Stream stream, ushort value) {
    Span<byte> bytes = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
    stream.Write(bytes);
  }

  internal static void WriteUInt24BigEndian(Stream stream, int value) {
    stream.WriteByte((byte)(value >> 16));
    stream.WriteByte((byte)(value >> 8));
    stream.WriteByte((byte)value);
  }

  internal static void WriteUInt32LittleEndian(Stream stream, uint value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
    stream.Write(bytes);
  }

  internal static void WriteInt32LittleEndian(Stream stream, int value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
    stream.Write(bytes);
  }

  internal static void WriteUInt32BigEndian(Stream stream, uint value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
    stream.Write(bytes);
  }

  internal static void WriteInt32BigEndian(Stream stream, int value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(bytes, value);
    stream.Write(bytes);
  }

  internal static void WriteUInt64LittleEndian(Stream stream, ulong value) {
    Span<byte> bytes = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
    stream.Write(bytes);
  }

  internal static void WriteUInt64BigEndian(Stream stream, ulong value) {
    Span<byte> bytes = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
    stream.Write(bytes);
  }

  internal static void WriteDoubleBigEndian(Stream stream, double value)
    => WriteUInt64BigEndian(stream, BitConverter.DoubleToUInt64Bits(value));

  internal static void WriteBox(Stream destination, string type, Action<MemoryStream>? writeBody = null) {
    using var body = new MemoryStream();
    writeBody?.Invoke(body);
    var size = checked((uint)(body.Length + 8));
    WriteUInt32BigEndian(destination, size);
    WriteAscii(destination, type);
    body.Position = 0;
    body.CopyTo(destination);
  }

  internal static void WriteRiffChunk(Stream destination, string id, ReadOnlySpan<byte> body) {
    WriteAscii(destination, id);
    WriteUInt32LittleEndian(destination, checked((uint)body.Length));
    destination.Write(body);
    if ((body.Length & 1) != 0)
      destination.WriteByte(0);
  }

  internal static void WriteRiffList(Stream destination, string type, Action<MemoryStream> writeChildren) {
    using var body = new MemoryStream();
    WriteAscii(body, type);
    writeChildren(body);
    WriteAscii(destination, "LIST");
    WriteUInt32LittleEndian(destination, checked((uint)body.Length));
    body.Position = 0;
    body.CopyTo(destination);
    if ((body.Length & 1) != 0)
      destination.WriteByte(0);
  }

  internal static void WriteIffChunk(Stream destination, string id, ReadOnlySpan<byte> body) {
    WriteAscii(destination, id);
    WriteUInt32BigEndian(destination, checked((uint)body.Length));
    destination.Write(body);
    if ((body.Length & 1) != 0)
      destination.WriteByte(0);
  }

  internal static void WriteEbml(Stream destination, uint id, ReadOnlySpan<byte> body) {
    WriteEbmlId(destination, id);
    WriteEbmlSize(destination, checked((ulong)body.Length));
    destination.Write(body);
  }

  internal static void WriteEbml(Stream destination, uint id, Action<MemoryStream> writeBody) {
    using var body = new MemoryStream();
    writeBody(body);
    WriteEbmlId(destination, id);
    WriteEbmlSize(destination, checked((ulong)body.Length));
    body.Position = 0;
    body.CopyTo(destination);
  }

  internal static void WriteEbmlUnsigned(Stream destination, uint id, ulong value) {
    Span<byte> bytes = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
    var first = 0;
    while (first < 7 && bytes[first] == 0)
      ++first;
    WriteEbml(destination, id, bytes[first..]);
  }

  internal static void WriteEbmlSigned(Stream destination, uint id, long value) {
    Span<byte> bytes = stackalloc byte[8];
    BinaryPrimitives.WriteInt64BigEndian(bytes, value);
    var first = 0;
    while (first < 7) {
      var current = bytes[first];
      var next = bytes[first + 1];
      if (current == 0x00 && (next & 0x80) == 0 || current == 0xFF && (next & 0x80) != 0)
        ++first;
      else
        break;
    }
    WriteEbml(destination, id, bytes[first..]);
  }

  internal static void WriteEbmlText(Stream destination, uint id, string value)
    => WriteEbml(destination, id, Encoding.UTF8.GetBytes(value));

  internal static void WriteEbmlFloat(Stream destination, uint id, double value) {
    Span<byte> bytes = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(bytes, BitConverter.DoubleToUInt64Bits(value));
    WriteEbml(destination, id, bytes);
  }

  internal static void WriteEbmlId(Stream destination, uint id) {
    var bytes = id <= 0xFF ? 1 : id <= 0xFFFF ? 2 : id <= 0xFFFFFF ? 3 : 4;
    for (var shift = (bytes - 1) * 8; shift >= 0; shift -= 8)
      destination.WriteByte((byte)(id >> shift));
  }

  internal static void WriteEbmlSize(Stream destination, ulong value) {
    var bytes = 1;
    while (bytes < 8 && value >= ((1UL << (7 * bytes)) - 1))
      ++bytes;

    if (bytes == 8 && value >= 0x00FFFFFFFFFFFFFFUL)
      throw new NotSupportedException("EBML element is too large for a finite 8-byte size.");

    var marker = 1UL << (7 * bytes);
    var encoded = marker | value;
    for (var shift = (bytes - 1) * 8; shift >= 0; shift -= 8)
      destination.WriteByte((byte)(encoded >> shift));
  }

  internal static long Rescale(long value, Rational from, long targetUnitsPerSecond) {
    if (!from.IsKnown)
      return value;
    if (targetUnitsPerSecond <= 0)
      throw new ArgumentOutOfRangeException(nameof(targetUnitsPerSecond));

    var result = (Int128)value * from.Numerator * targetUnitsPerSecond / from.Denominator;
    return checked((long)result);
  }

  internal static long UnitsPerSecond(Rational timeBase, long fallback = 1000) {
    if (!timeBase.IsKnown || timeBase.Numerator <= 0 || timeBase.Denominator <= 0)
      return fallback;

    var gcd = GreatestCommonDivisor(timeBase.Numerator, timeBase.Denominator);
    var numerator = timeBase.Numerator / gcd;
    var denominator = timeBase.Denominator / gcd;
    return numerator == 1 ? denominator : Math.Min(1_000_000_000L, denominator);
  }

  internal static long GreatestCommonDivisor(long a, long b) {
    a = Math.Abs(a);
    b = Math.Abs(b);
    while (b != 0)
      (a, b) = (b, a % b);
    return a == 0 ? 1 : a;
  }
}
