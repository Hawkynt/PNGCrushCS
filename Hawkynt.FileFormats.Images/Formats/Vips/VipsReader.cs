using System;
using System.IO;

namespace FileFormat.Vips;

/// <summary>Reads VIPS native image files from bytes, streams, or file paths.</summary>
public static class VipsReader {

  internal const int HeaderSize = VipsHeader.StructSize;
  internal const int MagicValue = VipsHeader.MagicValue;

  public static VipsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VIPS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VipsFile FromStream(Stream stream) {
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

  public static VipsFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < VipsHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid VIPS file.");

    var header = VipsHeader.ReadFrom(data.Slice(0, VipsHeader.StructSize));

    // A file written on a machine of the other byte order carries the magic reversed, and every
    // other field with it. Reversing the header back is what makes it readable; the samples follow
    // the same order, so anything wider than a byte is reversed as it is read.
    var isSwapped = header.Magic == VipsHeader.SwappedMagicValue;
    if (isSwapped) {
      var reordered = _ReverseFields(data[..VipsHeader.StructSize]);
      header = VipsHeader.ReadFrom(reordered);
    } else if (header.Magic != VipsHeader.MagicValue)
      throw new InvalidDataException($"Invalid VIPS magic: expected 0x{VipsHeader.MagicValue:X8}, got 0x{header.Magic:X8}.");

    if (header.Width <= 0)
      throw new InvalidDataException($"Invalid VIPS width: {header.Width}.");
    if (header.Height <= 0)
      throw new InvalidDataException($"Invalid VIPS height: {header.Height}.");
    if (header.Bands <= 0)
      throw new InvalidDataException($"Invalid VIPS band count: {header.Bands}.");

    var bandFormat = (VipsBandFormat)header.BandFormat;

    // A sixteen-bit band is ordinary — anything through a scanner or a colour-managed pipeline
    // carries one — so it is read and narrowed rather than refused. Wider and floating formats are
    // still declined, since narrowing those is a decision about range this has no basis to make.
    var bytesPerSample = bandFormat switch {
      VipsBandFormat.UChar or VipsBandFormat.Char => 1,
      VipsBandFormat.UShort or VipsBandFormat.Short => 2,
      _ => throw new NotSupportedException($"Only 8- and 16-bit band formats are supported, got {bandFormat}."),
    };

    var bytesPerPixel = header.Bands * bytesPerSample;
    var expectedPixelBytes = header.Width * header.Height * bytesPerPixel;
    var available = data.Length - VipsHeader.StructSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(VipsHeader.StructSize, copyLen).CopyTo(pixelData.AsSpan(0));

    return new VipsFile {
      IsSwapped = isSwapped,
      Width = header.Width,
      Height = header.Height,
      Bands = header.Bands,
      BandFormat = bandFormat,
      PixelData = pixelData,
    };
    }

  public static VipsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Reverses the header's fields, which are four bytes each but for two shorts.</summary>
  private static byte[] _ReverseFields(ReadOnlySpan<byte> header) {
    var reordered = header.ToArray();

    // Thirteen ints, then two shorts, then two more ints — the shorts are reversed in pairs so a
    // blanket four-byte swap would exchange them with each other.
    _Reverse(reordered, 0, 13, 4);
    _Reverse(reordered, 52, 2, 2);
    _Reverse(reordered, 56, 2, 4);

    return reordered;
  }

  private static void _Reverse(byte[] data, int offset, int count, int width) {
    for (var i = 0; i < count; ++i)
      Array.Reverse(data, offset + i * width, width);
  }
}
