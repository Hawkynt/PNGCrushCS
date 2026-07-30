using System;
using System.IO;

namespace FileFormat.SuperHiresEditor;

/// <summary>Reads Super-hires Editor I pictures from bytes, streams, or file paths.</summary>
public static class SuperHiresEditor1Reader {

  public static SuperHiresEditor1File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Super-hires picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SuperHiresEditor1File FromStream(Stream stream) {
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

  public static SuperHiresEditor1File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length == SuperHiresEditor1File.PlainFileSize)
      return new() {
        Data = data.ToArray(),
        BitmapOffset = 6690,
        VideoMatrixOffset = 448,
        ScreenStride = 40,
        ForegroundSpritesOffset = 4530,
        BackgroundSpritesOffset = 2482,
        ForegroundColorsOffset = 430,
        BackgroundColorsOffset = 426,
        RowShift = 2,
      };

    var unpacked = SuperHiresLayout.TryUnpack(data, SuperHiresEditor1File.UnpackedSize)
      ?? throw new InvalidDataException($"Not a Super-hires picture: {data.Length} bytes that do not unpack.");

    return new() {
      Data = unpacked,
      BitmapOffset = 0,
      VideoMatrixOffset = 6048,
      ScreenStride = 12,
      ForegroundSpritesOffset = 4032,
      BackgroundSpritesOffset = 2016,
      // One table for both layers, the foreground in the high nibble.
      ForegroundColorsOffset = 6300,
      BackgroundColorsOffset = 6300,
      RowShift = 0,
    };
  }

  public static SuperHiresEditor1File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
