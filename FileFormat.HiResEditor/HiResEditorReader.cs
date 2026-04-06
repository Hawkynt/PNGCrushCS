using System;
using System.IO;

namespace FileFormat.HiResEditor;

/// <summary>Reads Hires Editor files from bytes, streams, or file paths.</summary>
public static class HiResEditorReader {

  public static HiResEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiResEditorFile FromStream(Stream stream) {
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

  public static HiResEditorFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static HiResEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HiResEditorFile.ExpectedFileSize)
      throw new InvalidDataException($"Hires Editor file too small (got {data.Length} bytes, expected {HiResEditorFile.ExpectedFileSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += HiResEditorFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[HiResEditorFile.BitmapDataSize];
    data.AsSpan(offset, HiResEditorFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += HiResEditorFile.BitmapDataSize;

    // Screen RAM (1000 bytes)
    var screenData = new byte[HiResEditorFile.ScreenDataSize];
    data.AsSpan(offset, HiResEditorFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
    };
  }
}
