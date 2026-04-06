using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.MayaIff;

/// <summary>Reads Maya IFF files from bytes, streams, or file paths.</summary>
public static class MayaIffReader {

  /// <summary>FOR4 magic bytes (46 4F 52 34).</summary>
  private static readonly byte[] _FOR4_MAGIC = "FOR4"u8.ToArray();

  /// <summary>CIMG form type bytes (43 49 4D 47).</summary>
  private static readonly byte[] _CIMG_TYPE = "CIMG"u8.ToArray();

  /// <summary>TBHD chunk tag.</summary>
  private static readonly byte[] _TBHD_TAG = "TBHD"u8.ToArray();

  /// <summary>RGBA chunk tag.</summary>
  private static readonly byte[] _RGBA_TAG = "RGBA"u8.ToArray();

  /// <summary>RGB  chunk tag (with trailing space).</summary>
  private static readonly byte[] _RGB_TAG = Encoding.ASCII.GetBytes("RGB ");

  /// <summary>Minimum file size: 12 (FOR4+size+CIMG) + 8 (TBHD tag+size) + 32 (TBHD data) = 52.</summary>
  private const int _MIN_FILE_SIZE = 12 + 8 + 32;

  public static MayaIffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Maya IFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MayaIffFile FromStream(Stream stream) {
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

  public static MayaIffFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MayaIffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid Maya IFF file.");

    if (!data.AsSpan(0, 4).SequenceEqual(_FOR4_MAGIC))
      throw new InvalidDataException("Invalid Maya IFF magic: expected FOR4.");

    if (!data.AsSpan(8, 4).SequenceEqual(_CIMG_TYPE))
      throw new InvalidDataException("Invalid Maya IFF form type: expected CIMG.");

    var offset = 12;
    var width = 0;
    var height = 0;
    var tbhdFound = false;
    byte[]? pixelData = null;
    var hasAlpha = false;

    while (offset + 8 <= data.Length) {
      var chunkTag = data.AsSpan(offset, 4);
      var chunkSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4));
      var chunkDataOffset = offset + 8;

      if (chunkTag.SequenceEqual(_TBHD_TAG)) {
        if (chunkDataOffset + MayaIffTbhdHeader.StructSize > data.Length)
          throw new InvalidDataException("TBHD chunk data truncated.");

        var tbhd = MayaIffTbhdHeader.ReadFrom(data.AsSpan(chunkDataOffset, MayaIffTbhdHeader.StructSize));
        width = (int)tbhd.Width;
        height = (int)tbhd.Height;
        tbhdFound = true;
      } else if (pixelData == null && (chunkTag.SequenceEqual(_RGBA_TAG) || chunkTag.SequenceEqual(_RGB_TAG))) {
        hasAlpha = chunkTag.SequenceEqual(_RGBA_TAG);
        var pixelBytes = Math.Min(chunkSize, data.Length - chunkDataOffset);
        pixelData = new byte[pixelBytes];
        data.AsSpan(chunkDataOffset, pixelBytes).CopyTo(pixelData.AsSpan(0));
      }

      // Advance to next chunk, aligned to 4 bytes
      var paddedSize = (chunkSize + 3) & ~3;
      offset = chunkDataOffset + paddedSize;
    }

    if (!tbhdFound)
      throw new InvalidDataException("No TBHD chunk found in Maya IFF file.");

    return new MayaIffFile {
      Width = width,
      Height = height,
      HasAlpha = hasAlpha,
      PixelData = pixelData ?? [],
    };
  }
}
