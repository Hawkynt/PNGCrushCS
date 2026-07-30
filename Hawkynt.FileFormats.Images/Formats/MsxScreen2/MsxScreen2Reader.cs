using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxScreen2;

/// <summary>Reads MSX Screen 2 (TMS9918) image files from bytes, streams, or file paths.</summary>
public static class MsxScreen2Reader {

  public static MsxScreen2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MSX Screen 2 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxScreen2File FromStream(Stream stream) {
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

  public static MsxScreen2File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < MsxScreen2File.VramDataSize)
      throw new InvalidDataException($"Data too small for a valid MSX Screen 2 file: got {data.Length} bytes, need at least {MsxScreen2File.VramDataSize}.");

    var hasBsave = data.Length >= MsxScreen2File.BsaveHeaderSize + MsxScreen2File.VramDataSize && data[0] == MsxScreen2File.BsaveMagic;
    var rawOffset = hasBsave ? MsxScreen2File.BsaveHeaderSize : 0;

    if (data.Length - rawOffset < MsxScreen2File.VramDataSize)
      throw new InvalidDataException($"Data too small for MSX Screen 2 VRAM after header: got {data.Length - rawOffset} bytes, need {MsxScreen2File.VramDataSize}.");

    var span = data[rawOffset..];

    var patternGenerator = new byte[MsxScreen2File.PatternGeneratorSize];
    span[..MsxScreen2File.PatternGeneratorSize].CopyTo(patternGenerator);

    var colorTable = new byte[MsxScreen2File.ColorTableSize];
    span.Slice(MsxScreen2File.ColorTableOffset, MsxScreen2File.ColorTableSize).CopyTo(colorTable);

    var patternNameTable = new byte[MsxScreen2File.PatternNameTableSize];
    span.Slice(MsxScreen2File.PatternNameTableOffset, MsxScreen2File.PatternNameTableSize).CopyTo(patternNameTable);

    // Nothing marks a stored palette as present, so its own shape is the only evidence.
    var palette = MsxGraphics.HasPaletteAt(span, MsxScreen2File.PaletteOffset)
      ? span.Slice(MsxScreen2File.PaletteOffset, 32).ToArray()
      : null;

    return new() {
      PatternGenerator = patternGenerator,
      ColorTable = colorTable,
      PatternNameTable = patternNameTable,
      HasBsaveHeader = hasBsave,
      Palette = palette,
      Vram = span.Length >= MsxScreen2File.SpriteVramSize
        ? span[..MsxScreen2File.SpriteVramSize].ToArray()
        : null,
    };
    }

  public static MsxScreen2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MsxScreen2File.VramDataSize)
      throw new InvalidDataException($"Data too small for a valid MSX Screen 2 file: got {data.Length} bytes, need at least {MsxScreen2File.VramDataSize}.");

    var hasBsave = data.Length >= MsxScreen2File.BsaveHeaderSize + MsxScreen2File.VramDataSize && data[0] == MsxScreen2File.BsaveMagic;
    var rawOffset = hasBsave ? MsxScreen2File.BsaveHeaderSize : 0;

    if (data.Length - rawOffset < MsxScreen2File.VramDataSize)
      throw new InvalidDataException($"Data too small for MSX Screen 2 VRAM after header: got {data.Length - rawOffset} bytes, need {MsxScreen2File.VramDataSize}.");

    var span = data.AsSpan(rawOffset);

    var patternGenerator = new byte[MsxScreen2File.PatternGeneratorSize];
    span[..MsxScreen2File.PatternGeneratorSize].CopyTo(patternGenerator);

    var colorTable = new byte[MsxScreen2File.ColorTableSize];
    span.Slice(MsxScreen2File.ColorTableOffset, MsxScreen2File.ColorTableSize).CopyTo(colorTable);

    var patternNameTable = new byte[MsxScreen2File.PatternNameTableSize];
    span.Slice(MsxScreen2File.PatternNameTableOffset, MsxScreen2File.PatternNameTableSize).CopyTo(patternNameTable);

    // Nothing marks a stored palette as present, so its own shape is the only evidence.
    var palette = MsxGraphics.HasPaletteAt(span, MsxScreen2File.PaletteOffset)
      ? span.Slice(MsxScreen2File.PaletteOffset, 32).ToArray()
      : null;

    return new() {
      PatternGenerator = patternGenerator,
      ColorTable = colorTable,
      PatternNameTable = patternNameTable,
      HasBsaveHeader = hasBsave,
      Palette = palette,
      Vram = span.Length >= MsxScreen2File.SpriteVramSize
        ? span[..MsxScreen2File.SpriteVramSize].ToArray()
        : null,
    };
  }
}
