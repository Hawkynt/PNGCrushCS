using System;
using System.IO;

namespace FileFormat.SuperHiresEditor;

/// <summary>Reads Super Hires Editor (.she) files from bytes, streams, or file paths.</summary>
public static class SuperHiresEditorReader {

  public static SuperHiresEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Super Hires Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SuperHiresEditorFile FromStream(Stream stream) {
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

  public static SuperHiresEditorFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SuperHiresEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < SuperHiresEditorFile.LoadAddressSize + SuperHiresEditorFile.MinPayloadSize)
      throw new InvalidDataException($"File too small for Super Hires Editor format (got {data.Length} bytes, need at least {SuperHiresEditorFile.LoadAddressSize + SuperHiresEditorFile.MinPayloadSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += SuperHiresEditorFile.LoadAddressSize;

    // Bitmap 1 (8000 bytes)
    var bitmap1 = new byte[SuperHiresEditorFile.BitmapDataSize];
    data.AsSpan(offset, SuperHiresEditorFile.BitmapDataSize).CopyTo(bitmap1);
    offset += SuperHiresEditorFile.BitmapDataSize;

    // Screen 1 (1000 bytes)
    var screen1 = new byte[SuperHiresEditorFile.ScreenDataSize];
    data.AsSpan(offset, SuperHiresEditorFile.ScreenDataSize).CopyTo(screen1);
    offset += SuperHiresEditorFile.ScreenDataSize;

    // Bitmap 2 (8000 bytes)
    var bitmap2 = new byte[SuperHiresEditorFile.BitmapDataSize];
    data.AsSpan(offset, SuperHiresEditorFile.BitmapDataSize).CopyTo(bitmap2);
    offset += SuperHiresEditorFile.BitmapDataSize;

    // Screen 2 (1000 bytes)
    var screen2 = new byte[SuperHiresEditorFile.ScreenDataSize];
    data.AsSpan(offset, SuperHiresEditorFile.ScreenDataSize).CopyTo(screen2);
    offset += SuperHiresEditorFile.ScreenDataSize;

    // Any trailing data
    var trailingData = Array.Empty<byte>();
    if (offset < data.Length) {
      trailingData = new byte[data.Length - offset];
      data.AsSpan(offset).CopyTo(trailingData);
    }

    return new() {
      LoadAddress = loadAddress,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
      TrailingData = trailingData,
    };
  }
}
