using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Ipl;

/// <summary>Reads IPLab images from bytes, streams, or file paths.</summary>
public static class IplReader {

  public static IplFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IPL file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IplFile FromStream(Stream stream) {
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

  public static IplFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IplFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid IPLab file.");

    var magic = Encoding.ASCII.GetString(data[..4]);
    var isBigEndian = magic == IplFile.MotorolaMagic;
    if (!isBigEndian && magic != IplFile.IntelMagic)
      throw new InvalidDataException($"Not an IPLab file: magic '{magic}'.");

    if (Encoding.ASCII.GetString(data[8..12]) != IplFile.Version)
      throw new InvalidDataException($"Unsupported IPLab version '{Encoding.ASCII.GetString(data[8..12])}'.");

    if (Encoding.ASCII.GetString(data[12..16]) != IplFile.DataTag)
      throw new InvalidDataException($"IPLab file does not open with its picture: tag '{Encoding.ASCII.GetString(data[12..16])}'.");

    var width = _Read(data, 20, isBigEndian);
    var height = _Read(data, 24, isBigEndian);
    var channels = _Read(data, 28, isBigEndian);
    var type = _Read(data, 40, isBigEndian);

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid IPLab size {width}x{height}.");
    if (channels is <= 0 or > 4)
      throw new InvalidDataException($"An IPLab picture of {channels} channels is not one this reads.");

    // The type names the width of a sample. Only the two integer widths are pictures; the rest are
    // measurements that would need a range to be meaningful.
    var sampleBits = type switch {
      0 => 8,
      1 => 16,
      _ => throw new InvalidDataException($"IPLab sample type {type} is not one this reads."),
    };

    var expected = width * height * channels * (sampleBits / 8);
    var available = data.Length - IplFile.HeaderSize;
    if (available < expected)
      throw new InvalidDataException($"An IPLab {width}x{height} needs {expected} bytes of planes, but {available} follow.");

    return new() {
      Width = width,
      Height = height,
      Channels = channels,
      SampleBits = sampleBits,
      IsBigEndian = isBigEndian,
      PixelData = data.Slice(IplFile.HeaderSize, expected).ToArray(),
    };
  }

  private static int _Read(ReadOnlySpan<byte> data, int offset, bool isBigEndian)
    => isBigEndian
      ? BinaryPrimitives.ReadInt32BigEndian(data[offset..])
      : BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);

  public static IplFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
