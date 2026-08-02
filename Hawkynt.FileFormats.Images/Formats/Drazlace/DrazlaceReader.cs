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

  /// <summary>
  /// KNOWN WRONG. Reads the file, but not as a real Drazlace picture is laid out.
  /// </summary>
  /// <remarks>
  /// A real file was measured and this does not match it in three ways, each read off the bytes
  /// rather than guessed:
  /// <list type="bullet">
  /// <item>the load address is followed by the thirteen letters <c>DRAZLACE! 1.0</c>, which are not
  /// stepped over here and go into the unpacker as though they were picture;</item>
  /// <item>the byte after those letters is what the packing escapes on — 0xCB in the sample — and
  /// zero is assumed instead;</item>
  /// <item>what the packing expands to is 18001 bytes, two bitmaps either side of one screen plus a
  /// colour map and the background. The 19001 demanded here counts a second screen, and sharing one
  /// screen between the two bitmaps is the whole of what laces them together.</item>
  /// </list>
  /// Correcting all three still does not give the picture RECOIL draws — it goes from stripes to
  /// blocks in roughly the right colours — so at least one more thing is wrong and the corrections
  /// are not applied, rather than leaving the format half-changed against a writer that would then
  /// disagree with it. The measurements are here so the next attempt starts from them.
  /// </remarks>
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
