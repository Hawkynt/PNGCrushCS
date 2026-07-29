using System;
using System.IO;

namespace FileFormat.SamCoupeMode4;

/// <summary>Reads SAM Coupe mode 4 screens from bytes, streams, or file paths.</summary>
public static class SamCoupeMode4Reader {

  public static SamCoupeMode4File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SAM Coupe screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SamCoupeMode4File FromStream(Stream stream) {
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

  public static SamCoupeMode4File FromSpan(ReadOnlySpan<byte> data) {
    // Interrupt records make the length variable, so only the minimum is fixed.
    if (data.Length < SamCoupeMode4File.FileSize)
      throw new InvalidDataException(
        $"A SAM Coupe mode 4 screen is at least {SamCoupeMode4File.FileSize} bytes, got {data.Length}.");

    var bitmap = new byte[SamCoupeMode4File.BitmapDataSize];
    data[..SamCoupeMode4File.BitmapDataSize].CopyTo(bitmap);

    var palette = new byte[SamCoupePalette.EntryCount];
    data.Slice(SamCoupeMode4File.PaletteOffset, SamCoupePalette.EntryCount).CopyTo(palette);

    return new() { BitmapData = bitmap, Palette = palette };
  }

  public static SamCoupeMode4File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
