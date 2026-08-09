using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.FlashImage;

/// <summary>Writes Flash Image pictures (.fi) in the palette form.</summary>
public static class FlashImageWriter {

  public static byte[] ToBytes(FlashImageFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No picture to write.");

    var width = file.Width;
    var height = file.Height;
    if (width is < 1 or > 0xFFFF || height is < 1 or > 0xFFFF)
      throw new InvalidOperationException($"A Flash Image states its size in words, so {width}x{height} cannot be written.");

    var count = Math.Clamp(file.PaletteCount, 1, 256);
    var stride = FlashImageFile.RowStride(width);
    var raw = new byte[count * 3 + stride * height];
    file.Palette.AsSpan(0, Math.Min(file.Palette.Length, count * 3)).CopyTo(raw);
    for (var y = 0; y < height; ++y)
      file.PixelData.AsSpan(y * width, width).CopyTo(raw.AsSpan(count * 3 + y * stride));

    var payload = new MemoryStream();
    using (var zlib = new ZLibStream(payload, CompressionLevel.SmallestSize, true))
      zlib.Write(raw, 0, raw.Length);

    var result = new byte[FlashImageFile.HeaderSize + (int)payload.Length];
    FlashImageFile.Magic.CopyTo(result);
    _WriteWord(result, 4, width);
    _WriteWord(result, 6, height);
    _WriteWord(result, 8, FlashImageFile.IndexedMode);
    _WriteWord(result, 14, count);
    payload.GetBuffer().AsSpan(0, (int)payload.Length).CopyTo(result.AsSpan(FlashImageFile.HeaderSize));
    return result;
  }

  private static void _WriteWord(byte[] data, int at, int value) {
    data[at] = (byte)(value >> 8);
    data[at + 1] = (byte)value;
  }
}
