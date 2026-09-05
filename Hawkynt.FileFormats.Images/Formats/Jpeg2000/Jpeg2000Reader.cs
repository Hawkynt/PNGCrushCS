using System;
using System.IO;
using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000;

/// <summary>Reads JPEG 2000 files (JP2 container or raw J2K codestream) from bytes, streams, or file paths.</summary>
public static class Jpeg2000Reader {

  /// <summary>Twelve bytes is the JP2 signature box; a bare codestream needs at least SOC and SIZ.</summary>
  private const int _MINIMUM_SIZE = 12;

  public static Jpeg2000File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JPEG 2000 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Jpeg2000File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static Jpeg2000File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static Jpeg2000File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MINIMUM_SIZE)
      throw new InvalidDataException("Data too small for a valid JPEG 2000 file.");

    var bytes = data.ToArray();

    if (_IsJp2Container(bytes))
      return _ParseJp2(bytes);

    if (bytes[0] == 0xFF && bytes[1] == 0x4F)
      return _Decode(bytes, 0, bytes.Length);

    throw new InvalidDataException("Invalid JPEG 2000 signature: expected a JP2 box or a J2K SOC marker.");
  }

  private static bool _IsJp2Container(byte[] data) {
    if (data.Length < Jp2Box.JP2_SIGNATURE_BYTES.Length)
      return false;

    for (var i = 0; i < Jp2Box.JP2_SIGNATURE_BYTES.Length; ++i)
      if (data[i] != Jp2Box.JP2_SIGNATURE_BYTES[i])
        return false;

    return true;
  }

  private static Jpeg2000File _ParseJp2(byte[] data) {
    foreach (var box in Jp2Box.ReadBoxes(data, 0, data.Length))
      if (box.Type == Jp2Box.TYPE_CODESTREAM)
        return _Decode(box.Data, 0, box.Data.Length);

    throw new InvalidDataException("JP2 file has no contiguous codestream (jp2c) box.");
  }

  private static Jpeg2000File _Decode(byte[] data, int offset, int length) {
    var decoded = Jp2CodestreamDecoder.Decode(data, offset, length);

    // The public model carries eight-bit grey or RGB. A codestream with an alpha or a spot channel
    // still decodes; the extra components simply do not reach the raster.
    var componentCount = decoded.Components.Length >= 3 ? 3 : 1;
    var width = decoded.Width;
    var height = decoded.Height;
    var pixels = new byte[width * height * componentCount];

    for (var c = 0; c < componentCount; ++c) {
      var component = decoded.Components[c];
      var plane = decoded.Planes[c];
      var planeWidth = decoded.PlaneWidths[c];
      var planeHeight = decoded.PlaneHeights[c];
      if (planeWidth <= 0 || planeHeight <= 0)
        continue;

      // The same offset serves twice over: it undoes the DC level shift an unsigned component was
      // coded with, and it moves a signed component's range up into the one a raster can hold.
      var shift = 1 << (component.Precision - 1);
      var maximum = component.Precision >= 31 ? int.MaxValue : (1 << component.Precision) - 1;

      for (var y = 0; y < height; ++y) {
        var sourceRow = Math.Min(y / component.Dy, planeHeight - 1) * planeWidth;
        for (var x = 0; x < width; ++x) {
          var value = plane[sourceRow + Math.Min(x / component.Dx, planeWidth - 1)] + shift;
          value = Math.Clamp(value, 0, maximum);
          pixels[(y * width + x) * componentCount + c] = _ToByte(value, component.Precision);
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      ComponentCount = componentCount,
      BitsPerComponent = decoded.Components[0].Precision,
      DecompositionLevels = decoded.DecompositionLevels,
      PixelData = pixels,
    };
  }

  /// <summary>
  /// Brings one sample of the codestream's stated depth down to a byte. A deeper sample keeps its
  /// top bits; a shallower one is scaled so full scale stays full scale.
  /// </summary>
  private static byte _ToByte(int value, int bits) => bits switch {
    8 => (byte)value,
    > 8 => (byte)(value >> (bits - 8)),
    1 => value != 0 ? (byte)255 : (byte)0,
    _ => (byte)(value * 255 / ((1 << bits) - 1)),
  };
}
