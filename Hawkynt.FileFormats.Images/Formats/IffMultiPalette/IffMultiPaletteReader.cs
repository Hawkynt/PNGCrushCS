using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ilbm;

namespace FileFormat.IffMultiPalette;

/// <summary>Reads the PCHG specialization of IFF ILBM multi-palette pictures.</summary>
public static class IffMultiPaletteReader {

  private const int _MinimumIffSize = 12;

  public static IffMultiPaletteFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IFF multi-palette file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffMultiPaletteFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static IffMultiPaletteFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IffMultiPaletteFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MinimumIffSize)
      throw new InvalidDataException("Data is too small for an IFF multi-palette picture.");
    if (!data[..4].SequenceEqual("FORM"u8) || !data.Slice(8, 4).SequenceEqual("ILBM"u8))
      throw new InvalidDataException("An IFF multi-palette picture is a FORM ILBM carrying a PCHG property chunk.");
    if (!_ContainsChunk(data, "PCHG"u8))
      throw new InvalidDataException("IFF ILBM contains no PCHG palette-change chunk.");

    var ilbm = IlbmReader.FromSpan(data);
    if (ilbm.NumPlanes is < 1 or > 4)
      throw new InvalidDataException($"This IFF multi-palette reader supports one to four indexed bitplanes; the file carries {ilbm.NumPlanes}.");
    if (ilbm.IsHam)
      throw new InvalidDataException("HAM palette-change pictures belong to the sliced-HAM path; this multi-palette format is indexed.");
    if (ilbm.ScanlinePalettes is not { } palettes)
      throw new InvalidDataException("The PCHG chunk is compressed or uses a palette-change form this reader does not support.");

    var expectedPaletteBytes = checked(ilbm.Height * IffMultiPaletteFile.PaletteByteSize);
    if (palettes.Length < expectedPaletteBytes)
      throw new InvalidDataException("PCHG palette data ends before the last scanline.");

    var result = new IffMultiPaletteFile {
      Width = ilbm.Width,
      Height = ilbm.Height,
      PixelData = ilbm.PixelData[..],
      ScanlinePalettes = palettes.AsSpan(0, expectedPaletteBytes).ToArray(),
    };
    IffMultiPaletteFile.Validate(result, nameof(data));
    return result;
  }

  private static bool _ContainsChunk(ReadOnlySpan<byte> data, ReadOnlySpan<byte> wanted) {
    var formSize = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
    var end = (int)Math.Min((long)data.Length, 8L + formSize);

    for (var offset = 12; offset + 8 <= end;) {
      var size = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
      var payload = offset + 8L;
      var next = payload + size + (size & 1);
      if (next > end)
        return false;
      if (data.Slice(offset, 4).SequenceEqual(wanted))
        return true;
      offset = (int)next;
    }

    return false;
  }
}
