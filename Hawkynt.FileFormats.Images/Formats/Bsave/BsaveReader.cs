using System;
using System.IO;

namespace FileFormat.Bsave;

/// <summary>Reads BSAVE files from bytes, streams, or file paths.</summary>
public static class BsaveReader {

  public static BsaveFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BSAVE file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BsaveFile FromStream(Stream stream) {
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

  public static BsaveFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < BsaveHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid BSAVE file.");

    var header = BsaveHeader.ReadFrom(data);

    if (header.Magic != BsaveHeader.MagicValue)
      throw new InvalidDataException($"Invalid BSAVE magic byte: expected 0x{BsaveHeader.MagicValue:X2}, got 0x{header.Magic:X2}.");

    // One byte is not a signature. The header also states how long the saved block is, and a real
    // file carries that many bytes after it; checking only the byte claimed every file that happened
    // to begin with 0xFD — among them a VBXE slide show whose first dozen bytes are all 0xFD, which
    // then came back 320 by 200 instead of the 320 by 240 it holds.
    // A real file carries exactly the block it states — the sample here says 7836 and holds 7836.
    // A trailer of a few bytes is tolerated; anything further apart is not this format.
    var stated = header.Length;
    var carried = data.Length - BsaveHeader.StructSize;
    if (stated > 0 && (carried < stated || carried > stated + BsaveHeader.StructSize))
      throw new InvalidDataException($"A BSAVE block states {stated} bytes and the file carries {carried}, so this is not one.");

    var mode = _DetectMode(header.Segment, header.Length);
    var (width, height) = _GetDimensions(mode);

    var dataLength = data.Length - BsaveHeader.StructSize;
    var pixelData = new byte[dataLength];
    data.Slice(BsaveHeader.StructSize, dataLength).CopyTo(pixelData);

    return new BsaveFile {
      Width = width,
      Height = height,
      Mode = mode,
      PixelData = pixelData
    };
  }

  public static BsaveFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static BsaveMode _DetectMode(ushort segment, ushort length) {
    if (segment == 0xA000) {
      if (length >= 64000)
        return BsaveMode.Vga320x200x256;

      if (length >= 28000)
        return BsaveMode.Ega640x350x16;
    }

    if (segment == 0xB800) {
      // Exactly 16000 bytes → 80x100x1024 Reenigne mode (8000 char+attr cells, no padding).
      // 16384 stays on the CGA-1 path because that's what standard SCREEN 1 dumps look like.
      if (length == 16000)
        return BsaveMode.Cga80x100x1024;
      // 8000 bytes → 160x100x16 tweak mode (nibble-packed 4bpp).
      if (length is 8000 or 8192)
        return BsaveMode.Cga160x100x16;
      return BsaveMode.Cga320x200x4;
    }

    // Default fallback based on data length
    return length switch {
      >= 64000 => BsaveMode.Vga320x200x256,
      >= 28000 => BsaveMode.Ega640x350x16,
      16000 => BsaveMode.Cga80x100x1024,
      8000 or 8192 => BsaveMode.Cga160x100x16,
      _ => BsaveMode.Cga320x200x4
    };
  }

  private static (int Width, int Height) _GetDimensions(BsaveMode mode) => mode switch {
    BsaveMode.Cga320x200x4 => (320, 200),
    BsaveMode.Ega640x350x16 => (640, 350),
    BsaveMode.Vga320x200x256 => (320, 200),
    BsaveMode.Cga640x200x2 => (640, 200),
    _ => (320, 200)
  };
}
