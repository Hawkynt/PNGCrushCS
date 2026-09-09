using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ilbm;

namespace FileFormat.IffMultiPalette;

/// <summary>Assembles IFF ILBM files carrying uncompressed four-bit PCHG palette changes.</summary>
public static class IffMultiPaletteWriter {

  private const int _PCHG_HEADER_SIZE = 20;
  private const ushort _PCHGF_4BIT = 0x0001;

  public static byte[] ToBytes(IffMultiPaletteFile file) {
    if (file.PixelData is not { Length: > 0 } && file.RawData is { Length: > 0 })
      file = IffMultiPaletteReader.FromBytes(file.RawData);

    _Validate(file);

    var palettes = _NormalizePalettes(file.ScanlinePalettes, file.Height);
    var initialPalette = palettes[..IffMultiPaletteFile.PaletteBytes];
    var ilbm = new IlbmFile {
      Width = file.Width,
      Height = file.Height,
      NumPlanes = file.NumPlanes,
      Compression = IlbmCompression.None,
      Masking = IlbmMasking.None,
      TransparentColor = 0,
      XAspect = 1,
      YAspect = 1,
      PageWidth = file.Width,
      PageHeight = file.Height,
      PixelData = file.PixelData[..],
      Palette = initialPalette,
      ViewportMode = file.ViewportMode,
    };

    var baseIlbm = IlbmWriter.ToBytes(ilbm);
    var pchg = _BuildPchg(palettes, file.Height);
    return _InsertPchgBeforeBody(baseIlbm, pchg);
  }

  private static void _Validate(IffMultiPaletteFile file) {
    if (file.Width is <= 0 or > short.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(file), file.Width, $"PCHG writer requires a width between 1 and {short.MaxValue} pixels.");
    if (file.Height is <= 0 or > short.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(file), file.Height, $"PCHG writer requires a height between 1 and {short.MaxValue} pixels.");
    if (!(file.NumPlanes is >= 1 and <= 4 || file.NumPlanes == 6 && (file.ViewportMode & 0x800) != 0))
      throw new NotSupportedException($"PCHG writing supports 1..4 indexed bitplanes or HAM6, not {file.NumPlanes} planes with CAMG 0x{file.ViewportMode:X8}.");

    var pixels = checked(file.Width * file.Height);
    if (file.PixelData is not { } pixelData || pixelData.Length < pixels)
      throw new InvalidDataException($"MultiPalette pixel data is truncated: expected {pixels} bytes.");
    if (file.ScanlinePalettes is not { } palettes || palettes.Length < checked(file.Height * IffMultiPaletteFile.PaletteBytes))
      throw new InvalidDataException($"MultiPalette scanline palettes are truncated: expected {file.Height * IffMultiPaletteFile.PaletteBytes} bytes.");

    var maximumCode = (1 << file.NumPlanes) - 1;
    for (var i = 0; i < pixels; ++i)
      if (pixelData[i] > maximumCode)
        throw new InvalidDataException($"Pixel {i} uses code {pixelData[i]}, which does not fit in {file.NumPlanes} bitplanes.");
  }

  /// <summary>Quantizes every palette state to the RGB444 values the selected PCHG representation can state.</summary>
  private static byte[] _NormalizePalettes(byte[] source, int height) {
    var length = checked(height * IffMultiPaletteFile.PaletteBytes);
    var result = new byte[length];
    for (var i = 0; i < length; ++i)
      result[i] = _ToRgb4(source[i]);
    return result;
  }

  /// <summary>Builds an uncompressed PCHGF_4BIT change stream, enforcing the seven-write Copper limit.</summary>
  private static byte[] _BuildPchg(ReadOnlySpan<byte> palettes, int height) {
    var lineCount = Math.Max(0, height - 1);
    var maskBytes = ((lineCount + 31) / 32) * 4;
    var mask = new byte[maskBytes];
    using var records = new MemoryStream();

    var minRegister = IffMultiPaletteFile.PaletteEntries;
    var maxRegister = -1;
    Span<int> changed = stackalloc int[IffMultiPaletteFile.PaletteEntries];
    Span<byte> encoded = stackalloc byte[2];

    for (var line = 0; line < lineCount; ++line) {
      var previous = palettes.Slice(line * IffMultiPaletteFile.PaletteBytes, IffMultiPaletteFile.PaletteBytes);
      var current = palettes.Slice((line + 1) * IffMultiPaletteFile.PaletteBytes, IffMultiPaletteFile.PaletteBytes);
      var count = 0;

      for (var register = 0; register < IffMultiPaletteFile.PaletteEntries; ++register) {
        var at = register * 3;
        if (previous.Slice(at, 3).SequenceEqual(current.Slice(at, 3)))
          continue;
        changed[count++] = register;
      }

      if (count > IffMultiPaletteFile.MaxChangesPerLine)
        throw new InvalidDataException($"PCHG line {line + 1} changes {count} colour registers; Copper-displayable files permit at most {IffMultiPaletteFile.MaxChangesPerLine}.");
      if (count == 0)
        continue;

      mask[line >> 3] |= (byte)(0x80 >> (line & 7));
      records.WriteByte((byte)count);
      records.WriteByte(0); // no changes to registers 16..31

      for (var i = 0; i < count; ++i) {
        var register = changed[i];
        minRegister = Math.Min(minRegister, register);
        maxRegister = Math.Max(maxRegister, register);
        var at = register * 3;
        var word = (ushort)(
          register << 12
          | (current[at] / 0x11) << 8
          | (current[at + 1] / 0x11) << 4
          | current[at + 2] / 0x11
        );
        BinaryPrimitives.WriteUInt16BigEndian(encoded, word);
        records.Write(encoded);
      }
    }

    var recordBytes = records.ToArray();
    var originalSize = checked(maskBytes + recordBytes.Length);
    var result = new byte[checked(_PCHG_HEADER_SIZE + originalSize)];

    BinaryPrimitives.WriteUInt16BigEndian(result, 0); // PCHG_COMP_NONE
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), _PCHGF_4BIT);
    BinaryPrimitives.WriteInt16BigEndian(result.AsSpan(4), 1); // CMAP is line 0; changes begin at line 1
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(6), (ushort)lineCount);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(8), (ushort)(maxRegister < 0 ? 0 : minRegister));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(10), (ushort)Math.Max(0, maxRegister));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(12), 0); // no Huffman tree
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(14), 0);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(16), (uint)originalSize);
    mask.CopyTo(result, _PCHG_HEADER_SIZE);
    recordBytes.CopyTo(result, _PCHG_HEADER_SIZE + maskBytes);

    return result;
  }

  /// <summary>Inserts the property chunk immediately before BODY and fixes the enclosing FORM size.</summary>
  private static byte[] _InsertPchgBeforeBody(ReadOnlySpan<byte> ilbm, ReadOnlySpan<byte> pchg) {
    var bodyOffset = _FindBodyOffset(ilbm);
    var chunkSize = checked(8 + pchg.Length + (pchg.Length & 1));
    var result = new byte[checked(ilbm.Length + chunkSize)];

    ilbm[..bodyOffset].CopyTo(result);
    var at = bodyOffset;
    "PCHG"u8.CopyTo(result.AsSpan(at));
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at + 4), (uint)pchg.Length);
    pchg.CopyTo(result.AsSpan(at + 8));
    at += 8 + pchg.Length;
    if ((pchg.Length & 1) != 0)
      result[at++] = 0;
    ilbm[bodyOffset..].CopyTo(result.AsSpan(at));

    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)(result.Length - 8));
    return result;
  }

  private static int _FindBodyOffset(ReadOnlySpan<byte> ilbm) {
    if (ilbm.Length < 12 || !ilbm[..4].SequenceEqual("FORM"u8) || !ilbm.Slice(8, 4).SequenceEqual("ILBM"u8))
      throw new InvalidDataException("Internal ILBM writer produced an invalid FORM header.");

    var offset = 12;
    while (offset + 8 <= ilbm.Length) {
      var size = BinaryPrimitives.ReadUInt32BigEndian(ilbm[(offset + 4)..]);
      if (size > int.MaxValue)
        throw new InvalidDataException("Internal ILBM writer produced an invalid chunk size.");
      if (ilbm.Slice(offset, 4).SequenceEqual("BODY"u8))
        return offset;

      var length = (int)size;
      offset = checked(offset + 8 + length + (length & 1));
    }

    throw new InvalidDataException("Internal ILBM writer produced no BODY chunk.");
  }

  private static byte _ToRgb4(byte value) => (byte)(((value + 8) / 17) * 17);
}
