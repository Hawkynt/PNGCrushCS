using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Miff;

/// <summary>Assembles MIFF file bytes from pixel data.</summary>
public static class MiffWriter {

  public static byte[] ToBytes(MiffFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file);
  }

  private static byte[] _Assemble(MiffFile file) {
    using var ms = new MemoryStream();

    // Write header
    var header = MiffHeaderParser.Format(file);
    ms.Write(header);

    // Write palette for PseudoClass
    if (file.ColorClass == MiffColorClass.PseudoClass && file.Palette != null)
      ms.Write(file.Palette);

    // Compress and write pixel data
    var bytesPerChannel = file.Depth / 8;
    var channelsPerPixel = _GetChannelsPerPixel(file.Type, file.Colorspace);
    var bytesPerPixel = channelsPerPixel * bytesPerChannel;

    switch (file.Compression) {
      case MiffCompression.Rle:
        var rleData = MiffRleCompressor.Compress(file.PixelData, bytesPerPixel);
        ms.Write(rleData);
        break;
      case MiffCompression.Zip:
        var zipData = _CompressZip(file.PixelData);
        ms.Write(zipData);
        break;
      default:
        ms.Write(file.PixelData);
        break;
    }

    return ms.ToArray();
  }

  private static int _GetChannelsPerPixel(string type, string colorspace) {
    if (colorspace.Equals("CMYK", StringComparison.OrdinalIgnoreCase))
      return type.Contains("Alpha", StringComparison.OrdinalIgnoreCase) ? 5 : 4;

    if (type.Contains("Alpha", StringComparison.OrdinalIgnoreCase))
      return type.StartsWith("Grayscale", StringComparison.OrdinalIgnoreCase) ? 2 :
             type.StartsWith("Palette", StringComparison.OrdinalIgnoreCase) ? 2 : 4;

    if (type.StartsWith("Grayscale", StringComparison.OrdinalIgnoreCase))
      return 1;

    if (type.StartsWith("Palette", StringComparison.OrdinalIgnoreCase))
      return 1;

    return 3;
  }

  private static byte[] _CompressZip(byte[] data) {
    using var outputStream = new MemoryStream();
    using (var deflateStream = new DeflateStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
      deflateStream.Write(data);

    return outputStream.ToArray();
  }
}
