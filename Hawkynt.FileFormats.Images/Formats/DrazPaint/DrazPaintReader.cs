using System;
using System.IO;

namespace FileFormat.DrazPaint;

/// <summary>Reads DrazPaint (.drz) files from bytes, streams, or file paths.</summary>
public static class DrazPaintReader {

  public static DrazPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DrazPaint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DrazPaintFile FromStream(Stream stream) {
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
  /// Reads a DrazPaint picture.
  /// </summary>
  /// <remarks>
  /// Nothing verifies this. The only .drz in the corpus is not a DrazPaint picture at all — it is the
  /// same Vidcom 64 file the archive also files as .vid, byte for byte with one more on the end, and
  /// both tools draw it as one. So the run-length decoding below and the order of the sections after
  /// it are what a real file looks like only if the guess was right, and no sample says either way.
  /// <para/>
  /// A content check was considered and left out. The two formats cannot be told apart on what is
  /// known here — a Vidcom is 10050 bytes and this file is 10051, which is close enough to guess at
  /// and not close enough to build on, and guessing from a single misfiled sample is how a reader
  /// ends up claiming files that are not its own.
  /// </remarks>
  public static DrazPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < DrazPaintFile.LoadAddressSize + 1)
      throw new InvalidDataException($"Data too small for a valid DrazPaint file (got {data.Length} bytes).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var compressed = new byte[data.Length - DrazPaintFile.LoadAddressSize];
    data.Slice(DrazPaintFile.LoadAddressSize, compressed.Length).CopyTo(compressed.AsSpan(0));

    var decompressed = DrazPaintFile.RleDecode(compressed);
    if (decompressed.Length < DrazPaintFile.UncompressedPayloadSize)
      throw new InvalidDataException($"Decompressed data too small (expected at least {DrazPaintFile.UncompressedPayloadSize} bytes, got {decompressed.Length}).");

    var offset = 0;

    var bitmapData = new byte[DrazPaintFile.BitmapDataSize];
    decompressed.AsSpan(offset, DrazPaintFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += DrazPaintFile.BitmapDataSize;

    var screenRam = new byte[DrazPaintFile.ScreenRamSize];
    decompressed.AsSpan(offset, DrazPaintFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += DrazPaintFile.ScreenRamSize;

    var colorRam = new byte[DrazPaintFile.ColorRamSize];
    decompressed.AsSpan(offset, DrazPaintFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += DrazPaintFile.ColorRamSize;

    var backgroundColor = decompressed[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
    };
    }

  public static DrazPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
