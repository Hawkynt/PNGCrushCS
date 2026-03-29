using System;
using System.IO;

namespace FileFormat.CpcAdvanced;

/// <summary>Reads CPC Advanced Mode 0 images from bytes, streams, or file paths.</summary>
public static class CpcAdvancedReader {

  public static CpcAdvancedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CPC Advanced file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CpcAdvancedFile FromStream(Stream stream) {
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

  public static CpcAdvancedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != CpcAdvancedFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CPC Advanced data size: expected exactly {CpcAdvancedFile.ExpectedFileSize} bytes, got {data.Length}.");

    // Deinterleave CPC memory layout: Line Y address = ((Y / 8) * 80) + ((Y % 8) * 2048)
    var linearData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow];
    for (var y = 0; y < CpcAdvancedFile.PixelHeight; ++y) {
      var srcOffset = (y / 8) * CpcAdvancedFile.BytesPerRow + (y % 8) * 2048;
      var dstOffset = y * CpcAdvancedFile.BytesPerRow;
      data.AsSpan(srcOffset, CpcAdvancedFile.BytesPerRow).CopyTo(linearData.AsSpan(dstOffset));
    }

    return new CpcAdvancedFile { PixelData = linearData };
  }
}
