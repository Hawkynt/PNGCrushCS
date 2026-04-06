using System;
using System.IO;

namespace FileFormat.XFliEditor;

/// <summary>Reads X-FLI Editor (.xfl) files from bytes, streams, or file paths.</summary>
public static class XFliEditorReader {

  public static XFliEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("X-FLI Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XFliEditorFile FromStream(Stream stream) {
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

  public static XFliEditorFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static XFliEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Length < XFliEditorFile.LoadAddressSize + XFliEditorFile.MinPayloadSize)
      throw new InvalidDataException($"File too small for X-FLI Editor format (got {data.Length} bytes, need at least {XFliEditorFile.LoadAddressSize + XFliEditorFile.MinPayloadSize}).");

    var offset = 0;

    // Load address (2 bytes, little-endian)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += XFliEditorFile.LoadAddressSize;

    // Bitmap data (8000 bytes)
    var bitmapData = new byte[XFliEditorFile.BitmapDataSize];
    data.AsSpan(offset, XFliEditorFile.BitmapDataSize).CopyTo(bitmapData);
    offset += XFliEditorFile.BitmapDataSize;

    // 8 screen banks (8 x 1000 bytes)
    var screenBanks = new byte[XFliEditorFile.ScreenBankCount][];
    for (var i = 0; i < XFliEditorFile.ScreenBankCount; ++i) {
      screenBanks[i] = new byte[XFliEditorFile.ScreenBankSize];
      data.AsSpan(offset, XFliEditorFile.ScreenBankSize).CopyTo(screenBanks[i]);
      offset += XFliEditorFile.ScreenBankSize;
    }

    // Color RAM (1000 bytes)
    var colorData = new byte[XFliEditorFile.ColorDataSize];
    data.AsSpan(offset, XFliEditorFile.ColorDataSize).CopyTo(colorData);
    offset += XFliEditorFile.ColorDataSize;

    // Background color: next byte if available, else 0
    byte backgroundColor = 0;
    var trailingData = Array.Empty<byte>();
    if (offset < data.Length) {
      backgroundColor = data[offset];
      ++offset;
      if (offset < data.Length) {
        trailingData = new byte[data.Length - offset];
        data.AsSpan(offset).CopyTo(trailingData);
      }
    }

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenBanks = screenBanks,
      ColorData = colorData,
      BackgroundColor = backgroundColor,
      TrailingData = trailingData,
    };
  }
}
