using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.KodakDc25;

/// <summary>Writes the DC25 sensor into a big-endian TIFF-shaped camera file.</summary>
public static class KodakDc25Writer {

  private const ushort _TypeAscii = 2;
  private const ushort _TagMake = 271;
  private const ushort _TagModel = 272;

  public static byte[] ToBytes(KodakDc25File file) {
    var sensor = file.SensorData ?? throw new ArgumentException("A Kodak DC25 file needs raw sensor samples.", nameof(file));
    var sensorWidth = file.IsWideSensor ? KodakDc25File.WideSensorWidth : KodakDc25File.NarrowSensorWidth;
    var expected = checked(sensorWidth * KodakDc25File.SensorHeight);
    if (sensor.Length != expected)
      throw new ArgumentException($"A {sensorWidth}-wide DC25 sensor needs exactly {expected} bytes, got {sensor.Length}.", nameof(file));

    var output = new byte[checked(KodakDc25File.SensorOffset + expected)];
    _WriteMinimalTiffHeader(output);
    sensor.CopyTo(output, KodakDc25File.SensorOffset);
    return output;
  }

  private static void _WriteMinimalTiffHeader(byte[] output) {
    // A real TIFF header/IFD rather than a byte pattern hidden in padding. dcraw identifies this
    // generation by the Model tag, then uses the known fixed sensor offset for DC25/DC2x cameras.
    output[0] = (byte)'M';
    output[1] = (byte)'M';
    BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(2), 42);
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4), 8);

    const int ifd = 8;
    const int entries = 2;
    const int entryBytes = 12;
    const int values = ifd + 2 + entries * entryBytes + 4;
    var make = Encoding.ASCII.GetBytes("KODAK\0");
    var model = Encoding.ASCII.GetBytes(KodakDc25File.Model + "\0");
    var makeAt = values;
    var modelAt = makeAt + make.Length;

    BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(ifd), entries);
    _AsciiEntry(output.AsSpan(ifd + 2, entryBytes), _TagMake, make.Length, makeAt);
    _AsciiEntry(output.AsSpan(ifd + 2 + entryBytes, entryBytes), _TagModel, model.Length, modelAt);
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(ifd + 2 + entries * entryBytes), 0);
    make.CopyTo(output, makeAt);
    model.CopyTo(output, modelAt);
  }

  private static void _AsciiEntry(Span<byte> entry, ushort tag, int count, int offset) {
    BinaryPrimitives.WriteUInt16BigEndian(entry, tag);
    BinaryPrimitives.WriteUInt16BigEndian(entry[2..], _TypeAscii);
    BinaryPrimitives.WriteUInt32BigEndian(entry[4..], checked((uint)count));
    BinaryPrimitives.WriteUInt32BigEndian(entry[8..], checked((uint)offset));
  }
}
