using System;
using System.IO;

namespace FileFormat.CpcOverscan;

/// <summary>Reads CPC overscan images from bytes, streams, or file paths.</summary>
public static class CpcOverscanReader {

  /// <summary>Size of each interleaved memory bank.</summary>
  private const int _BANK_SIZE = 16384;

  public static CpcOverscanFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CPC overscan file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CpcOverscanFile FromStream(Stream stream) {
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

  public static CpcOverscanFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != CpcOverscanFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CPC overscan data size: expected exactly {CpcOverscanFile.ExpectedFileSize} bytes, got {data.Length}.");

    // Deinterleave: two 16KB banks, each with 8 groups of 2048 bytes
    // Within each bank, line Y address = ((Y / 8) * BytesPerRow) + ((Y % 8) * 2048)
    // First bank contains lines 0-135, second bank contains lines 136-271
    var linearData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow];
    var linesPerBank = CpcOverscanFile.PixelHeight / 2;

    for (var bank = 0; bank < 2; ++bank) {
      var bankOffset = bank * _BANK_SIZE;
      for (var y = 0; y < linesPerBank; ++y) {
        var srcOffset = bankOffset + (y / 8) * CpcOverscanFile.BytesPerRow + (y % 8) * 2048;
        var dstY = bank * linesPerBank + y;
        if (dstY >= CpcOverscanFile.PixelHeight)
          break;

        var dstOffset = dstY * CpcOverscanFile.BytesPerRow;
        var copyLen = Math.Min(CpcOverscanFile.BytesPerRow, data.Length - srcOffset);
        if (copyLen > 0)
          data.Slice(srcOffset, copyLen).CopyTo(linearData.AsSpan(dstOffset));
      }
    }

    return new CpcOverscanFile { PixelData = linearData };
    }

  public static CpcOverscanFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != CpcOverscanFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid CPC overscan data size: expected exactly {CpcOverscanFile.ExpectedFileSize} bytes, got {data.Length}.");

    // Deinterleave: two 16KB banks, each with 8 groups of 2048 bytes
    // Within each bank, line Y address = ((Y / 8) * BytesPerRow) + ((Y % 8) * 2048)
    // First bank contains lines 0-135, second bank contains lines 136-271
    var linearData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow];
    var linesPerBank = CpcOverscanFile.PixelHeight / 2;

    for (var bank = 0; bank < 2; ++bank) {
      var bankOffset = bank * _BANK_SIZE;
      for (var y = 0; y < linesPerBank; ++y) {
        var srcOffset = bankOffset + (y / 8) * CpcOverscanFile.BytesPerRow + (y % 8) * 2048;
        var dstY = bank * linesPerBank + y;
        if (dstY >= CpcOverscanFile.PixelHeight)
          break;

        var dstOffset = dstY * CpcOverscanFile.BytesPerRow;
        var copyLen = Math.Min(CpcOverscanFile.BytesPerRow, data.Length - srcOffset);
        if (copyLen > 0)
          data.AsSpan(srcOffset, copyLen).CopyTo(linearData.AsSpan(dstOffset));
      }
    }

    return new CpcOverscanFile { PixelData = linearData };
  }
}
