using System;
using System.IO;

namespace FileFormat.SuperHiresEditor;

/// <summary>Reads Super-hires Editor II pictures from bytes, streams, or file paths.</summary>
public static class SuperHiresEditor2Reader {

  public static SuperHiresEditor2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Super-hires picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SuperHiresEditor2File FromStream(Stream stream) {
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

  public static SuperHiresEditor2File FromSpan(ReadOnlySpan<byte> data) {
    // A plain file is exactly one size; anything else is taken as packed, which is what the editor
    // wrote by default.
    if (data.Length == SuperHiresEditor2File.PlainFileSize)
      return new() {
        Data = data.ToArray(),
        BitmapOffset = 6642,
        VideoMatrixOffset = 442,
        ScreenStride = 40,
        SpritesOffset = 2482,
        SpriteColorsOffset = 426,
        ColumnSprites = false,
      };

    var unpacked = SuperHiresLayout.TryUnpack(data, SuperHiresEditor2File.UnpackedSize)
      ?? throw new InvalidDataException($"Not a Super-hires picture: {data.Length} bytes that do not unpack.");

    return new() {
      Data = unpacked,
      BitmapOffset = 0,
      VideoMatrixOffset = 8064,
      ScreenStride = 24,
      SpritesOffset = 4032,
      SpriteColorsOffset = 8568,
      ColumnSprites = true,
    };
  }

  public static SuperHiresEditor2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
