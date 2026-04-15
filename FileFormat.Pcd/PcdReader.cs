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
        throw new InvalidDataException("Invalid PCD magic at offset 2048: expected \"PCD_IPI\0\".");

    var magicEnd = PcdFile.PreambleSize + PcdFile.Magic.Length;
    var header = PcdHeader.ReadFrom(data[magicEnd..]);
    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width == 0 || height == 0)
      throw new InvalidDataException("PCD image dimensions must be positive.");

    var pixelDataOffset = PcdFile.HeaderSize;
    var expectedPixelBytes = width * height * 3;
    var available = data.Length - pixelDataOffset;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(pixelDataOffset, copyLen).CopyTo(pixelData.AsSpan(0));

    return new PcdFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static PcdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
