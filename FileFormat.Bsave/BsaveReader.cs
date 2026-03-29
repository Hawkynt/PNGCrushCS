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

  public static BsaveFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < BsaveHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid BSAVE file.");

    var header = BsaveHeader.ReadFrom(data.AsSpan());

    if (header.Magic != BsaveHeader.MagicValue)
      throw new InvalidDataException($"Invalid BSAVE magic byte: expected 0x{BsaveHeader.MagicValue:X2}, got 0x{header.Magic:X2}.");

    var mode = _DetectMode(header.Segment, header.Length);
    var (width, height) = _GetDimensions(mode);

    var dataLength = data.Length - BsaveHeader.StructSize;
    var pixelData = new byte[dataLength];
    data.AsSpan(BsaveHeader.StructSize, dataLength).CopyTo(pixelData.AsSpan(0));

    return new BsaveFile {
      Width = width,
      Height = height,
      Mode = mode,
      PixelData = pixelData
    };
  }

  private static BsaveMode _DetectMode(ushort segment, ushort length) {
    if (segment == 0xA000) {
      if (length >= 64000)
        return BsaveMode.Vga320x200x256;

      if (length >= 28000)
        return BsaveMode.Ega640x350x16;
    }

    if (segment == 0xB800)
      return BsaveMode.Cga320x200x4;

    // Default fallback based on data length
    return length switch {
      >= 64000 => BsaveMode.Vga320x200x256,
      >= 28000 => BsaveMode.Ega640x350x16,
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
