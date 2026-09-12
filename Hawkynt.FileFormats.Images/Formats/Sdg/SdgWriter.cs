using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Sdg;

/// <summary>Writes bitmap objects to a StarOffice/LibreOffice Gallery SDG object stream.</summary>
public static class SdgWriter {

  private const uint _ZCompress = 0x01004453;
  private const uint _BitmapExMagic1 = 0x25091962;
  private const uint _BitmapExMagic2 = 0xACB20201;
  private const int _ObjectHeaderSize = 11;
  private const int _BmpFileHeaderSize = 14;
  private const int _BmpInfoHeaderSize = 40;
  private const int _CompressedInfoSize = 12;

  public static byte[] ToBytes(SdgFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Images.Count == 0)
      throw new InvalidDataException("An SDG file needs at least one bitmap gallery object.");

    using var output = new MemoryStream();
    foreach (var image in file.Images) {
      ArgumentNullException.ThrowIfNull(image);
      _WriteBitmapObject(output, image);
    }

    return output.ToArray();
  }

  private static void _WriteBitmapObject(Stream output, RawImage image) {
    Span<byte> header = stackalloc byte[_ObjectHeaderSize];
    header[0] = (byte)'S';
    header[1] = (byte)'G';
    header[2] = (byte)'A';
    header[3] = (byte)'3';
    BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 4);
    BinaryPrimitives.WriteUInt16LittleEndian(header[6..], 5);
    BinaryPrimitives.WriteUInt16LittleEndian(header[8..], 1);
    header[10] = 1;
    output.Write(header);

    var bgra = image.EnsureFormat(PixelFormat.Bgra32);
    var hasTransparency = _HasTransparency(bgra.PixelData);
    _WriteCompressedBitmap(output, bgra.EnsureFormat(PixelFormat.Bgr24));

    Span<byte> bitmapEx = stackalloc byte[9];
    BinaryPrimitives.WriteUInt32LittleEndian(bitmapEx, _BitmapExMagic1);
    BinaryPrimitives.WriteUInt32LittleEndian(bitmapEx[4..], _BitmapExMagic2);
    bitmapEx[8] = hasTransparency ? (byte)2 : (byte)0;
    output.Write(bitmapEx);

    if (hasTransparency)
      _WriteCompressedBitmap(output, _CreateTransparencyMask(bgra));

    // Empty URL, ten legacy bitmap fields, empty legacy string, empty title.
    Span<byte> trailer = stackalloc byte[16];
    trailer.Clear();
    output.Write(trailer);
  }

  private static void _WriteCompressedBitmap(Stream output, RawImage image) {
    var bitmap = BmpWriter.ToBytes(BmpFile.FromRawImage(image));
    output.Write(_CompressBitmap(bitmap));
  }

  private static RawImage _CreateTransparencyMask(RawImage bgra) {
    var pixelCount = checked(bgra.Width * bgra.Height);
    var pixels = new byte[pixelCount];
    for (var i = 0; i < pixelCount; ++i)
      pixels[i] = (byte)(255 - bgra.PixelData[i * 4 + 3]);

    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i)
      palette.AsSpan(i * 3, 3).Fill((byte)i);

    return new RawImage {
      Width = bgra.Width,
      Height = bgra.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 256
    };
  }

  private static bool _HasTransparency(byte[] bgra) {
    for (var i = 3; i < bgra.Length; i += 4)
      if (bgra[i] != 0xFF)
        return true;

    return false;
  }

  private static byte[] _CompressBitmap(byte[] bitmap) {
    var minimumSize = _BmpFileHeaderSize + _BmpInfoHeaderSize;
    if (bitmap.Length < minimumSize || bitmap[0] != (byte)'B' || bitmap[1] != (byte)'M')
      throw new InvalidDataException("The BMP encoder returned an invalid bitmap.");
    if (BinaryPrimitives.ReadUInt32LittleEndian(bitmap.AsSpan(_BmpFileHeaderSize)) != _BmpInfoHeaderSize)
      throw new InvalidDataException("SDG ZCOMPRESS writing requires a BITMAPINFOHEADER bitmap.");

    var originalCompression = BinaryPrimitives.ReadUInt32LittleEndian(bitmap.AsSpan(30));
    if (originalCompression > 3)
      throw new InvalidDataException($"SDG ZCOMPRESS cannot wrap BMP compression {originalCompression}.");

    byte[] coded;
    using (var compressed = new MemoryStream()) {
      using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        zlib.Write(bitmap.AsSpan(minimumSize));
      coded = compressed.ToArray();
    }

    var result = new byte[checked(minimumSize + _CompressedInfoSize + coded.Length)];
    bitmap.AsSpan(0, minimumSize).CopyTo(result);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(2), checked((uint)result.Length));
    // ZCOMPRESS reads palette/masks from the inflated payload and ignores bfOffBits. Keeping the
    // stored offset inside the physical stream also avoids old readers rejecting a tiny indexed mask
    // whose uncompressed palette offset happens to be beyond the compressed byte count.
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(10), checked((uint)minimumSize));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(30), _ZCompress);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(minimumSize), checked((uint)coded.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(minimumSize + 4), checked((uint)(bitmap.Length - minimumSize)));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(minimumSize + 8), originalCompression);
    coded.CopyTo(result, minimumSize + _CompressedInfoSize);
    return result;
  }
}
