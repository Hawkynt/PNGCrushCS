using System;
using System.IO;

namespace FileFormat.PaintMagic;

/// <summary>Reads Paint Magic C64 multicolor files from bytes, streams, or file paths.</summary>
public static class PaintMagicReader {

  public static PaintMagicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Paint Magic file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PaintMagicFile FromStream(Stream stream) {
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

  public static PaintMagicFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PaintMagicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < PaintMagicFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Paint Magic file (expected {PaintMagicFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != PaintMagicFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Paint Magic file size (expected {PaintMagicFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += PaintMagicFile.LoadAddressSize;

    var bitmapData = new byte[PaintMagicFile.BitmapDataSize];
    data.AsSpan(offset, PaintMagicFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += PaintMagicFile.BitmapDataSize;

    var videoMatrix = new byte[PaintMagicFile.VideoMatrixSize];
    data.AsSpan(offset, PaintMagicFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += PaintMagicFile.VideoMatrixSize;

    var colorRam = new byte[PaintMagicFile.ColorRamSize];
    data.AsSpan(offset, PaintMagicFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += PaintMagicFile.ColorRamSize;

    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
    };
  }
}
