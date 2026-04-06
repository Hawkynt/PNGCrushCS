using System;
using System.IO;

namespace FileFormat.CpcPlus;

/// <summary>Reads CPC Plus Mode 1 images from bytes, streams, or file paths.</summary>
public static class CpcPlusReader {

  public static CpcPlusFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CPC Plus file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CpcPlusFile FromStream(Stream stream) {
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

  public static CpcPlusFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CpcPlusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != CpcPlusFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CPC Plus data size: expected exactly {CpcPlusFile.ExpectedFileSize} bytes, got {data.Length}.");

    // Deinterleave CPC memory layout for the screen data portion
    var linearData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow];
    for (var y = 0; y < CpcPlusFile.PixelHeight; ++y) {
      var srcOffset = (y / 8) * CpcPlusFile.BytesPerRow + (y % 8) * 2048;
      var dstOffset = y * CpcPlusFile.BytesPerRow;
      data.AsSpan(srcOffset, CpcPlusFile.BytesPerRow).CopyTo(linearData.AsSpan(dstOffset));
    }

    // Read palette data from after screen data
    var paletteData = new byte[CpcPlusFile.PaletteDataSize];
    data.AsSpan(CpcPlusFile.ScreenDataSize, CpcPlusFile.PaletteDataSize).CopyTo(paletteData.AsSpan(0));

    return new CpcPlusFile {
      PixelData = linearData,
      PaletteData = paletteData,
    };
  }
}
