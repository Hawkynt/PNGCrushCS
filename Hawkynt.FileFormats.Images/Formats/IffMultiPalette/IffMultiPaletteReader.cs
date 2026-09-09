using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ilbm;

namespace FileFormat.IffMultiPalette;

/// <summary>Reads IFF ILBM pictures carrying uncompressed four-bit PCHG palette changes.</summary>
public static class IffMultiPaletteReader {

  private const int _PCHG_HEADER_SIZE = 20;
  private const ushort _PCHGF_4BIT = 0x0001;
  private const ushort _PCHGF_32BIT = 0x0002;

  public static IffMultiPaletteFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MultiPalette file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffMultiPaletteFile FromStream(Stream stream) {
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

  public static IffMultiPaletteFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Reads the PCHG specialization of an ILBM image.</summary>
  public static IffMultiPaletteFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IffMultiPaletteFile.MinFileSize)
      throw new InvalidDataException($"Invalid MultiPalette data: expected at least {IffMultiPaletteFile.MinFileSize} bytes, got {data.Length}.");

    if (!data[..4].SequenceEqual("FORM"u8))
      throw new InvalidDataException("Not a MultiPalette picture: an IFF file begins with FORM.");
    if (!data.Slice(8, 4).SequenceEqual("ILBM"u8))
      throw new InvalidDataException("Not a MultiPalette picture: PCHG is a property of FORM ILBM, not a separate form type.");

    var formSize = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
    if (formSize < 4 || formSize > data.Length - 8)
      throw new InvalidDataException("Invalid MultiPalette FORM size.");
    var formEnd = checked((int)(formSize + 8));

    var pchg = _FindPchg(data[..formEnd]);
    if (pchg.IsEmpty)
      throw new InvalidDataException("Not a MultiPalette picture: the ILBM contains no PCHG chunk.");
    if (pchg.Length < _PCHG_HEADER_SIZE)
      throw new InvalidDataException("Invalid PCHG chunk: header is truncated.");

    var compression = BinaryPrimitives.ReadUInt16BigEndian(pchg);
    var flags = BinaryPrimitives.ReadUInt16BigEndian(pchg[2..]);
    if (compression != 0)
      throw new NotSupportedException("Compressed PCHG palette changes are not supported.");
    if ((flags & _PCHGF_32BIT) != 0)
      throw new NotSupportedException("32-bit PCHG palette changes are not supported by the 16-register MultiPalette reader.");
    if ((flags & _PCHGF_4BIT) == 0)
      throw new InvalidDataException("Invalid PCHG chunk: no supported palette-change format is selected.");

    var originalSize = BinaryPrimitives.ReadUInt32BigEndian(pchg[16..]);
    if (originalSize != pchg.Length - _PCHG_HEADER_SIZE)
      throw new InvalidDataException("Invalid uncompressed PCHG chunk: OriginalSize does not match the line data.");

    var ilbm = IlbmReader.FromSpan(data[..formEnd]);
    if (ilbm.Palette is not { Length: > 0 } palette)
      throw new InvalidDataException("Invalid MultiPalette picture: PCHG requires an initial CMAP palette.");
    if (!(ilbm.NumPlanes is >= 1 and <= 4 || ilbm.NumPlanes == 6 && ilbm.IsHam))
      throw new NotSupportedException($"PCHG with {ilbm.NumPlanes} bitplanes is not supported by the 16-register MultiPalette reader.");

    var scanlinePalettes = _DecodePaletteChanges(pchg, palette, ilbm.Height);

    return new() {
      Width = ilbm.Width,
      Height = ilbm.Height,
      NumPlanes = ilbm.NumPlanes,
      ViewportMode = ilbm.ViewportMode,
      PixelData = ilbm.PixelData[..],
      Palette = palette[..],
      ScanlinePalettes = scanlinePalettes,
      RawData = data[..formEnd].ToArray(),
    };
  }

  /// <summary>Finds the PCHG payload while respecting IFF chunk sizes and word padding.</summary>
  private static ReadOnlySpan<byte> _FindPchg(ReadOnlySpan<byte> data) {
    var offset = 12;
    while (offset + 8 <= data.Length) {
      var size = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
      if (size > int.MaxValue)
        throw new InvalidDataException("Invalid IFF chunk size.");

      var length = (int)size;
      var payload = offset + 8;
      if (length > data.Length - payload)
        throw new InvalidDataException("Invalid IFF chunk: payload extends beyond FORM data.");

      if (data.Slice(offset, 4).SequenceEqual("PCHG"u8))
        return data.Slice(payload, length);

      offset = checked(payload + length + (length & 1));
    }

    return default;
  }

  /// <summary>Expands an uncompressed PCHGF_4BIT change stream to one 16-register RGB palette per line.</summary>
  private static byte[] _DecodePaletteChanges(ReadOnlySpan<byte> pchg, ReadOnlySpan<byte> cmap, int height) {
    var startLine = BinaryPrimitives.ReadInt16BigEndian(pchg[4..]);
    var lineCount = BinaryPrimitives.ReadUInt16BigEndian(pchg[6..]);
    var maxRegister = BinaryPrimitives.ReadUInt16BigEndian(pchg[10..]);
    if (maxRegister >= IffMultiPaletteFile.PaletteEntries)
      throw new NotSupportedException($"PCHG uses colour register {maxRegister}; this MultiPalette representation supports registers 0..{IffMultiPaletteFile.PaletteEntries - 1}.");

    var maskBytes = checked(((lineCount + 31) / 32) * 4);
    if (pchg.Length < _PCHG_HEADER_SIZE + maskBytes)
      throw new InvalidDataException("Invalid PCHG chunk: line mask is truncated.");

    var current = new byte[IffMultiPaletteFile.PaletteBytes];
    cmap[..Math.Min(cmap.Length, current.Length)].CopyTo(current);
    var result = new byte[checked(height * IffMultiPaletteFile.PaletteBytes)];
    var recordAt = _PCHG_HEADER_SIZE + maskBytes;
    var visibleLine = 0;

    for (var line = 0; line < lineCount; ++line) {
      var actualLine = startLine + line;
      while (visibleLine < height && visibleLine < actualLine) {
        current.CopyTo(result, visibleLine * IffMultiPaletteFile.PaletteBytes);
        ++visibleLine;
      }

      var maskAt = _PCHG_HEADER_SIZE + (line >> 3);
      var changes = (pchg[maskAt] & (0x80 >> (line & 7))) != 0;
      if (changes) {
        if (recordAt + 2 > pchg.Length)
          throw new InvalidDataException("Invalid PCHG chunk: line-change count is truncated.");

        var small = pchg[recordAt];
        var big = pchg[recordAt + 1];
        recordAt += 2;
        if (big != 0)
          throw new NotSupportedException("PCHG changes to colour registers 16..31 are not supported by the 16-register MultiPalette reader.");

        var bytes = checked((small + big) * 2);
        if (recordAt + bytes > pchg.Length)
          throw new InvalidDataException("Invalid PCHG chunk: palette-change data is truncated.");

        for (var i = 0; i < small; ++i, recordAt += 2) {
          var change = BinaryPrimitives.ReadUInt16BigEndian(pchg[recordAt..]);
          var register = change >> 12;
          var at = register * 3;
          current[at] = (byte)(((change >> 8) & 0x0F) * 0x11);
          current[at + 1] = (byte)(((change >> 4) & 0x0F) * 0x11);
          current[at + 2] = (byte)((change & 0x0F) * 0x11);
        }
      }

      if (actualLine is >= 0 && actualLine < height) {
        current.CopyTo(result, actualLine * IffMultiPaletteFile.PaletteBytes);
        visibleLine = Math.Max(visibleLine, actualLine + 1);
      }
    }

    while (visibleLine < height) {
      current.CopyTo(result, visibleLine * IffMultiPaletteFile.PaletteBytes);
      ++visibleLine;
    }

    if (recordAt != pchg.Length)
      throw new InvalidDataException("Invalid PCHG chunk: change records do not consume the declared payload.");

    return result;
  }
}
