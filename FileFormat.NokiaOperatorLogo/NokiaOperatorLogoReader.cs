using System;
using System.IO;

namespace FileFormat.NokiaOperatorLogo;

/// <summary>Reads Nokia Operator Logo (NOL) files from bytes, streams, or file paths.</summary>
public static class NokiaOperatorLogoReader {

  public static NokiaOperatorLogoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NOL file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NokiaOperatorLogoFile FromStream(Stream stream) {
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

  public static NokiaOperatorLogoFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static NokiaOperatorLogoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NokiaOperatorLogoFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid NOL file (need at least {NokiaOperatorLogoFile.MinFileSize} bytes, got {data.Length}).");

    if (data[0] != NokiaOperatorLogoFile.Magic[0] || data[1] != NokiaOperatorLogoFile.Magic[1] || data[2] != NokiaOperatorLogoFile.Magic[2])
      throw new InvalidDataException("Invalid NOL magic bytes.");

    // Header layout (20 bytes total):
    // [0..2]  "NOL" magic
    // [3]     0x00
    // [4..5]  unknown (typically 0x01 0x00)
    // [6..7]  MCC as little-endian uint16: data[6] + 256*data[7]
    // [8]     MNC
    // [9]     padding
    // [10]    width
    // [11]    padding
    // [12]    height
    // [13]    padding
    // [14..19] unknown (typically 0x01 0x00 0x01 0x00 0x00 0x00)
    var mcc = data[6] | (data[7] << 8);
    var mnc = (int)data[8];
    var width = (int)data[10];
    var height = (int)data[12];

    if (width == 0 || height == 0)
      throw new InvalidDataException($"Invalid NOL dimensions: {width}x{height}.");

    var pixelCount = width * height;
    var expectedSize = NokiaOperatorLogoFile.HeaderSize + pixelCount;
    if (data.Length < expectedSize)
      throw new InvalidDataException($"NOL file truncated: expected at least {expectedSize} bytes for {width}x{height} pixel data, got {data.Length}.");

    // Pixel data is ASCII '0'/'1', one character per pixel, row-major.
    // Convert to 1bpp packed MSB-first with row padding to byte boundary.
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var srcIndex = NokiaOperatorLogoFile.HeaderSize + y * width + x;
        if (data[srcIndex] == (byte)'1') {
          var byteIndex = y * bytesPerRow + x / 8;
          var bitIndex = 7 - (x % 8);
          pixelData[byteIndex] |= (byte)(1 << bitIndex);
        }
      }

    return new() {
      Width = width,
      Height = height,
      Mcc = mcc,
      Mnc = mnc,
      PixelData = pixelData,
    };
  }
}
