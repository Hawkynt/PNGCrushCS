using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Mda;

/// <summary>Reads MicroDesign Area (.MDA) monochrome bitmap files.</summary>
public static class MdaReader {

  private const int _PrefixSize = MdaFile.StampSize + 4;

  public static MdaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MicroDesign Area file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MdaFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static MdaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static MdaFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _PrefixSize)
      throw new InvalidDataException("Truncated MicroDesign Area header.");

    if (!data[..4].SequenceEqual(".MDA"u8))
      throw new InvalidDataException("Invalid MicroDesign Area file type.");
    if (!data.Slice(4, 14).SequenceEqual("MicroDesignPCW"u8))
      throw new InvalidDataException("Invalid MicroDesign Area program identifier.");

    MdaVersion version;
    var versionBytes = data.Slice(18, 5);
    if (versionBytes.SequenceEqual("v1.00"u8))
      version = MdaVersion.Area2;
    else if (versionBytes.SequenceEqual("v1.30"u8))
      version = MdaVersion.Area3;
    else
      throw new InvalidDataException("Unsupported MicroDesign Area file version.");

    if (data[23] != 13 || data[24] != 10 || data[32] != 13 || data[33] != 10)
      throw new InvalidDataException("Invalid MicroDesign Area stamp line endings.");

    var serialBytes = data.Slice(25, MdaFile.SerialNumberLength);
    foreach (var value in serialBytes)
      if (value is < 0x20 or > 0x7E)
        throw new InvalidDataException("MicroDesign Area serial number is not printable ASCII.");

    foreach (var value in data.Slice(34, MdaFile.StampSize - 34))
      if (value != 0)
        throw new InvalidDataException("MicroDesign Area reserved stamp bytes must be zero.");

    var height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(128, 2));
    var widthBytes = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(130, 2));
    if (height == 0 || (height & 3) != 0)
      throw new InvalidDataException("MicroDesign Area height must be a positive multiple of four.");
    if (widthBytes == 0)
      throw new InvalidDataException("MicroDesign Area width must be positive.");

    var width = widthBytes * 8;
    if ((long)width * height > MdaFile.MaximumPixels)
      throw new InvalidDataException($"MicroDesign Area image exceeds the {MdaFile.MaximumPixels:N0}-pixel implementation safety limit.");

    var rasterLength = checked(widthBytes * height);
    var payload = data[_PrefixSize..];
    var raster = version == MdaVersion.Area2
      ? _DecodeArea2(payload, rasterLength)
      : _DecodeArea3(payload, widthBytes, height);

    return new MdaFile {
      Width = width,
      Height = height,
      Version = version,
      SerialNumber = Encoding.ASCII.GetString(serialBytes),
      RasterData = raster,
    };
  }

  private static byte[] _DecodeArea2(ReadOnlySpan<byte> payload, int rasterLength) {
    var raster = new byte[rasterLength];
    var source = 0;
    var destination = 0;

    while (destination < raster.Length) {
      if ((uint)source >= (uint)payload.Length)
        throw new InvalidDataException("Truncated MicroDesign AREA2 raster.");

      var value = payload[source++];
      if (value is not (0x00 or 0xFF)) {
        raster[destination++] = value;
        continue;
      }

      if ((uint)source >= (uint)payload.Length)
        throw new InvalidDataException("Truncated MicroDesign AREA2 run.");
      var encodedCount = payload[source++];
      var count = encodedCount == 0 ? 256 : encodedCount;
      if (count > raster.Length - destination)
        throw new InvalidDataException("MicroDesign AREA2 run exceeds the declared raster size.");

      raster.AsSpan(destination, count).Fill(value);
      destination += count;
    }

    if (source != payload.Length)
      throw new InvalidDataException("Unexpected trailing MicroDesign AREA2 data.");

    return raster;
  }

  private static byte[] _DecodeArea3(ReadOnlySpan<byte> payload, int widthBytes, int height) {
    var raster = new byte[checked(widthBytes * height)];
    var source = 0;

    for (var y = 0; y < height; ++y) {
      if ((uint)source >= (uint)payload.Length)
        throw new InvalidDataException($"Truncated MicroDesign AREA3 line {y}.");

      var row = raster.AsSpan(y * widthBytes, widthBytes);
      switch (payload[source++]) {
        case 0x00:
          if ((uint)source >= (uint)payload.Length)
            throw new InvalidDataException($"Truncated MicroDesign AREA3 all-same line {y}.");
          row.Fill(payload[source++]);
          break;

        case 0x01:
          _DecodeBlocks(payload, ref source, row, y);
          break;

        case 0x02: {
          if (y == 0)
            throw new InvalidDataException("MicroDesign AREA3 first line cannot use difference encoding.");

          _DecodeBlocks(payload, ref source, row, y);
          var previous = raster.AsSpan((y - 1) * widthBytes, widthBytes);
          for (var x = 0; x < widthBytes; ++x)
            row[x] ^= previous[x];
          break;
        }

        default:
          throw new InvalidDataException($"Unsupported MicroDesign AREA3 line type on line {y}.");
      }
    }

    if (source != payload.Length)
      throw new InvalidDataException("Unexpected trailing MicroDesign AREA3 data.");

    return raster;
  }

  private static void _DecodeBlocks(ReadOnlySpan<byte> payload, ref int source, Span<byte> destination, int line) {
    var written = 0;
    while (written < destination.Length) {
      if ((uint)source >= (uint)payload.Length)
        throw new InvalidDataException($"Truncated MicroDesign AREA3 block on line {line}.");

      var control = payload[source++];
      if (control == 0x80)
        throw new InvalidDataException($"Reserved MicroDesign AREA3 block control 0x80 on line {line}.");

      if (control <= 0x7F) {
        var count = control + 1;
        if (count > destination.Length - written)
          throw new InvalidDataException($"MicroDesign AREA3 literal block overruns line {line}.");
        if (count > payload.Length - source)
          throw new InvalidDataException($"Truncated MicroDesign AREA3 literal block on line {line}.");

        payload.Slice(source, count).CopyTo(destination[written..]);
        source += count;
        written += count;
        continue;
      }

      var repeat = 257 - control;
      if (repeat > destination.Length - written)
        throw new InvalidDataException($"MicroDesign AREA3 repeat block overruns line {line}.");
      if ((uint)source >= (uint)payload.Length)
        throw new InvalidDataException($"Truncated MicroDesign AREA3 repeat block on line {line}.");

      destination.Slice(written, repeat).Fill(payload[source++]);
      written += repeat;
    }
  }
}
