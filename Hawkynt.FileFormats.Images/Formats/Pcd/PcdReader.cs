using System;
using System.IO;

namespace FileFormat.Pcd;

/// <summary>Reads PCD (Kodak Photo CD) files from bytes, streams, or file paths.</summary>
public static class PcdReader {

  public static PcdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PCD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PcdFile FromStream(Stream stream) {
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

  public static PcdFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PcdFile.HeaderSize)
      throw new InvalidDataException($"Data too small for PCD: expected at least {PcdFile.HeaderSize} bytes, got {data.Length}.");

    for (var i = 0; i < PcdFile.Magic.Length; ++i)
      if (data[PcdFile.PreambleSize + i] != PcdFile.Magic[i])
        throw new InvalidDataException("Invalid PCD magic at offset 2048: expected \"PCD_IPI\".");

    // Photo CD holds no width or height anywhere: its images come at fixed resolutions and the Base
    // one is always 768x512. What used to be read as two 16-bit dimensions just after the magic is a
    // specification version and padding, so every file "measured" zero and was rejected for having
    // dimensions that were not positive. The magic itself was also being looked for as eight bytes
    // including a trailing NUL, when it is the seven characters "PCD_IPI" followed by that version.
    //
    // Colour is Photo YCC. Neutral greys come out matching ImageMagick exactly; strongly saturated
    // ones land close but not identical, because a faithful conversion also applies Photo YCC's
    // transfer curve rather than this linear matrix alone.
    const int width = BaseWidth;
    const int height = BaseHeight;
    const int chromaWidth = BaseWidth / 2;

    // The planes are not stored one after another but interleaved a row-group at a time: two luma
    // rows, then one Cb row and one Cr row covering both of them, since the chroma is at half
    // resolution on each axis.
    const int groupSize = (width * 2) + (chromaWidth * 2);
    if (BaseImageOffset + (groupSize * (height / 2)) > data.Length)
      throw new InvalidDataException("PCD file is too short to hold its Base resolution image.");

    var pixelData = new byte[width * height * 3];
    for (var y = 0; y < height; ++y) {
      var group = BaseImageOffset + ((y / 2) * groupSize);
      var lumaRow = group + ((y % 2) * width);
      var cbRow = group + (width * 2);
      var crRow = cbRow + chromaWidth;

      for (var x = 0; x < width; ++x) {
        var luma = data[lumaRow + x];
        var cb = data[cbRow + (x / 2)] - 156;
        var cr = data[crRow + (x / 2)] - 137;

        var at = (((y * width) + x) * 3);
        pixelData[at] = _Clamp(luma + (1.8215 * cr));
        pixelData[at + 1] = _Clamp(luma - (0.4302 * cb) - (0.9271 * cr));
        pixelData[at + 2] = _Clamp(luma + (2.2179 * cb));
      }
    }

    return new PcdFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  /// <summary>The Base resolution, which is the one every Photo CD carries.</summary>
  internal const int BaseWidth = 768;
  internal const int BaseHeight = 512;

  /// <summary>Where the Base image's first row group starts.</summary>
  internal const int BaseImageOffset = 0x30000;

  private static byte _Clamp(double value)
    => value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)value;

  public static PcdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
