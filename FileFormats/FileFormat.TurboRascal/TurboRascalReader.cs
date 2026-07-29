using System;
using System.IO;

namespace FileFormat.TurboRascal;

/// <summary>Reads Turbo Rascal Syntax Error (.flf) images from bytes, streams, or file paths.</summary>
public static class TurboRascalReader {

  public static TurboRascalFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TurboRascalFile FromStream(Stream stream) {
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

  public static TurboRascalFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < TurboRascalFile.PaletteOffset
        || !data[..TurboRascalFile.Signature.Length].SequenceEqual(TurboRascalFile.Signature))
      throw new InvalidDataException("Not an FLF file: missing the 'FLUFF64' signature.");

    var mode = data[TurboRascalFile.ModeOffset];
    if (mode != TurboRascalFile.ChunkyMode)
      throw new NotSupportedException($"FLF mode {mode} is not supported; only the chunky mode {TurboRascalFile.ChunkyMode} is.");

    // A stored count of zero means the full 256 entries.
    var colors = data[TurboRascalFile.ColorCountOffset];
    var entries = colors == 0 ? 256 : colors;
    if (data.Length < TurboRascalFile.PaletteOffset + entries * 3)
      throw new InvalidDataException($"FLF palette is truncated: expected {entries} entries.");

    var pixels = new byte[TurboRascalFile.PixelDataSize];
    data.Slice(TurboRascalFile.PixelDataOffset, TurboRascalFile.PixelDataSize).CopyTo(pixels);

    var palette = new byte[entries * 3];
    data.Slice(TurboRascalFile.PaletteOffset, palette.Length).CopyTo(palette);

    return new() { PixelData = pixels, Palette = palette };
  }

  public static TurboRascalFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
