using System;
using System.IO;

namespace FileFormat.Drazlace;

/// <summary>Reads Drazlace (.dlp/.drl) files from bytes, streams, or file paths.</summary>
public static class DrazlaceReader {

  public static DrazlaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Drazlace file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DrazlaceFile FromStream(Stream stream) {
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

  public static DrazlaceFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < DrazlaceFile.LoadAddressSize + 1)
      throw new InvalidDataException($"Data too small for a valid Drazlace file (got {data.Length} bytes).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var compressed = new byte[data.Length - DrazlaceFile.LoadAddressSize];
    data.Slice(DrazlaceFile.LoadAddressSize, compressed.Length).CopyTo(compressed.AsSpan(0));

    var decompressed = DrazlaceFile.RleDecode(compressed);
    if (decompressed.Length < DrazlaceFile.UncompressedPayloadSize)
      throw new InvalidDataException($"Decompressed data too small (expected at least {DrazlaceFile.UncompressedPayloadSize} bytes, got {decompressed.Length}).");

    var offset = 0;

    var bitmapData1 = new byte[DrazlaceFile.BitmapDataSize];
    decompressed.AsSpan(offset, DrazlaceFile.BitmapDataSize).CopyTo(bitmapData1.AsSpan(0));
    offset += DrazlaceFile.BitmapDataSize;

    var screenRam1 = new byte[DrazlaceFile.ScreenRamSize];
    decompressed.AsSpan(offset, DrazlaceFile.ScreenRamSize).CopyTo(screenRam1.AsSpan(0));
    offset += DrazlaceFile.ScreenRamSize;

    var colorRam = new byte[DrazlaceFile.ColorRamSize];
    decompressed.AsSpan(offset, DrazlaceFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += DrazlaceFile.ColorRamSize;

    var backgroundColor = decompressed[offset];
    offset += 1;

    var bitmapData2 = new byte[DrazlaceFile.BitmapDataSize];
    decompressed.AsSpan(offset, DrazlaceFile.BitmapDataSize).CopyTo(bitmapData2.AsSpan(0));
    offset += DrazlaceFile.BitmapDataSize;

    var screenRam2 = new byte[DrazlaceFile.ScreenRamSize];
    decompressed.AsSpan(offset, DrazlaceFile.ScreenRamSize).CopyTo(screenRam2.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData1 = bitmapData1,
      ScreenRam1 = screenRam1,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
      BitmapData2 = bitmapData2,
      ScreenRam2 = screenRam2,
    };
    }

  public static DrazlaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
