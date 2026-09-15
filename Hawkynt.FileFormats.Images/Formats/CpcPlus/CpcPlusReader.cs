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

  public static CpcPlusFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static CpcPlusFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != CpcPlusFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CPC Plus data size: expected exactly {CpcPlusFile.ExpectedFileSize} bytes, got {data.Length}.");

    // Deinterleave CPC memory layout for the screen data portion
    var linearData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow];
    for (var y = 0; y < CpcPlusFile.PixelHeight; ++y) {
      var srcOffset = (y / 8) * CpcPlusFile.BytesPerRow + (y % 8) * 2048;
      var dstOffset = y * CpcPlusFile.BytesPerRow;
      data.Slice(srcOffset, CpcPlusFile.BytesPerRow).CopyTo(linearData.AsSpan(dstOffset));
    }

    // Read palette data from after screen data
    var paletteData = new byte[CpcPlusFile.PaletteDataSize];
    data.Slice(CpcPlusFile.ScreenDataSize, CpcPlusFile.PaletteDataSize).CopyTo(paletteData.AsSpan(0));

    return new CpcPlusFile {
      PixelData = linearData,
      PaletteData = paletteData,
    };
    }

  public static CpcPlusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
