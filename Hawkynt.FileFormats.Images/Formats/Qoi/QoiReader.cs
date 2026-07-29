using System;
using System.IO;

namespace FileFormat.Qoi;

/// <summary>Reads QOI files from bytes, streams, or file paths.</summary>
public static class QoiReader {

  public static QoiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("QOI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static QoiFile FromStream(Stream stream) {
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

  public static QoiFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < QoiHeader.StructSize + 8)
      throw new InvalidDataException("Data too small for a valid QOI file.");

    var header = QoiHeader.ReadFrom(data);
    if (header.Magic1 != (byte)'q' || header.Magic2 != (byte)'o' || header.Magic3 != (byte)'i' || header.Magic4 != (byte)'f')
      throw new InvalidDataException("Invalid QOI signature.");

    if (header.Width == 0 || header.Height == 0)
      throw new InvalidDataException("QOI image dimensions must be non-zero.");

    var channels = header.Channels;
    if (channels != QoiChannels.Rgb && channels != QoiChannels.Rgba)
      throw new InvalidDataException($"Invalid QOI channel count: {(byte)channels}.");

    var encodedData = new byte[data.Length - QoiHeader.StructSize - 8];
    data.Slice(QoiHeader.StructSize, encodedData.Length).CopyTo(encodedData.AsSpan(0));

    var pixelData = QoiCodec.Decode(encodedData, (int)header.Width, (int)header.Height, channels);

    return new QoiFile {
      Width = (int)header.Width,
      Height = (int)header.Height,
      Channels = channels,
      ColorSpace = header.ColorSpace,
      PixelData = pixelData
    };
    }

  public static QoiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < QoiHeader.StructSize + 8)
      throw new InvalidDataException("Data too small for a valid QOI file.");

    var header = QoiHeader.ReadFrom(data.AsSpan());
    if (header.Magic1 != (byte)'q' || header.Magic2 != (byte)'o' || header.Magic3 != (byte)'i' || header.Magic4 != (byte)'f')
      throw new InvalidDataException("Invalid QOI signature.");

    if (header.Width == 0 || header.Height == 0)
      throw new InvalidDataException("QOI image dimensions must be non-zero.");

    var channels = header.Channels;
    if (channels != QoiChannels.Rgb && channels != QoiChannels.Rgba)
      throw new InvalidDataException($"Invalid QOI channel count: {(byte)channels}.");

    var encodedData = new byte[data.Length - QoiHeader.StructSize - 8];
    data.AsSpan(QoiHeader.StructSize, encodedData.Length).CopyTo(encodedData.AsSpan(0));

    var pixelData = QoiCodec.Decode(encodedData, (int)header.Width, (int)header.Height, channels);

    return new QoiFile {
      Width = (int)header.Width,
      Height = (int)header.Height,
      Channels = channels,
      ColorSpace = header.ColorSpace,
      PixelData = pixelData
    };
  }
}
