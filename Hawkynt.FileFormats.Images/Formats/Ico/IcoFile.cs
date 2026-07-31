using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Ico;

/// <summary>In-memory representation of an ICO file.</summary>
[FormatMagicBytes([0x00, 0x00, 0x01, 0x00])]
[FormatMimeType("image/vnd.microsoft.icon", "image/x-icon", "image/icon")]
public sealed class IcoFile : IImageFormatReader<IcoFile>, IImageToRawImage<IcoFile>, IImageFormatWriter<IcoFile>, IMultiImageFileFormat<IcoFile> {

  static string IImageFormatMetadata<IcoFile>.PrimaryExtension => ".ico";
  static string[] IImageFormatMetadata<IcoFile>.FileExtensions => [".ico"];
  static IcoFile IImageFormatReader<IcoFile>.FromSpan(ReadOnlySpan<byte> data) => IcoReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<IcoFile>.Capabilities => FormatCapability.HasDedicatedOptimizer | FormatCapability.MultiImage;
  static byte[] IImageFormatWriter<IcoFile>.ToBytes(IcoFile file) => IcoWriter.ToBytes(file);
  public IReadOnlyList<IcoImage> Images { get; init; } = [];

  /// <summary>Returns the number of image entries in this ICO file.</summary>
  public static int ImageCount(IcoFile file) => file.Images.Count;

  /// <summary>Converts the image entry at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IcoFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Images.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var entry = file.Images[index];
    return entry.Format == IcoImageFormat.Png
      ? PngFile.ToRawImage(PngReader.FromBytes(entry.Data))
      : _DecodeDib(entry);
  }

  /// <summary>Converts the largest image entry of an ICO file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IcoFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Images.Count == 0)
      throw new ArgumentException("ICO file contains no images.", nameof(file));

    var best = file.Images
      .OrderByDescending(i => i.Width * i.Height)
      .ThenByDescending(i => i.BitsPerPixel)
      .First();

    return best.Format == IcoImageFormat.Png
      ? PngFile.ToRawImage(PngReader.FromBytes(best.Data))
      : _DecodeDib(best);
  }

  /// <summary>
  /// Decodes the BMP-flavoured half of an icon entry — every colour depth Windows has shipped, from
  /// the 1-bit icons of Windows 3.0 to the 32-bit ones of today — into straight BGRA.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The DIB inside an icon is not quite a BMP. Its header claims twice the real height, because two
  /// bitmaps are stacked in it: the colour image, and below it a 1-bit AND mask saying which pixels
  /// the desktop shows through. Every depth below 32 carries its transparency there and nowhere else,
  /// so a decoder that reads only the colour half returns a correct picture with a wrong, opaque
  /// background — which is why everything here ends up as BGRA rather than staying indexed. A palette
  /// has no way to say "transparent".
  /// </para>
  /// <para>
  /// The 32-bit case keeps its own alpha channel, with one concession to reality: icons written
  /// before XP settled the convention often fill BGRA and leave every alpha byte zero, which read
  /// literally is a wholly invisible icon. Where the alpha channel is uniformly zero the AND mask is
  /// believed instead, which is what Windows itself does.
  /// </para>
  /// </remarks>
  private static RawImage _DecodeDib(IcoImage entry) {
    var dib = entry.Data;
    if (dib.Length < 12)
      throw new InvalidDataException("ICO entry is too small to hold a DIB header.");

    var biSize = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(0));

    // BITMAPCOREHEADER (12 bytes) counts its fields in 16 bits and has no palette-size field; every
    // later header is BITMAPINFOHEADER-shaped, whatever extra it appends.
    var isCore = biSize == 12;
    if (biSize < 12 || biSize > dib.Length)
      throw new InvalidDataException($"ICO entry declares a {biSize}-byte DIB header it does not contain.");

    int dibWidth, dibHeight, bitCount, compression, colourCount;
    if (isCore) {
      dibWidth = BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(4));
      dibHeight = BinaryPrimitives.ReadInt16LittleEndian(dib.AsSpan(8));
      bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(10));
      compression = 0;
      colourCount = 0;
    } else {
      if (dib.Length < 40)
        throw new InvalidDataException("ICO entry is too small for a BITMAPINFOHEADER.");

      dibWidth = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4));
      dibHeight = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8));
      bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
      compression = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(16));
      colourCount = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(32));
    }

    if (compression is not (0 or 3))
      throw new NotSupportedException($"ICO entries are stored uncompressed; this one declares compression {compression}.");

    var width = dibWidth > 0 ? dibWidth : entry.Width;
    var totalHeight = Math.Abs(dibHeight);

    // The doubled height is the norm; a writer that reports the true height simply has no mask.
    var height = totalHeight == entry.Height || totalHeight <= 1 ? totalHeight : totalHeight / 2;
    if (height <= 0)
      height = entry.Height;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"ICO entry has a nonsensical size of {width}x{height}.");

    if (bitCount is not (1 or 2 or 4 or 8 or 16 or 24 or 32))
      throw new NotSupportedException($"ICO BMP DIB with {bitCount} bits per pixel is not supported.");

    // Palette entries are four bytes wide except under the ancient core header, where they are three.
    var paletteStride = isCore ? 3 : 4;
    var paletteEntries = bitCount <= 8 ? (colourCount > 0 ? colourCount : 1 << bitCount) : 0;
    var pixelOffset = biSize + (paletteEntries * paletteStride);

    var palette = new byte[Math.Max(paletteEntries, 1) * 3];
    for (var i = 0; i < paletteEntries; ++i) {
      var at = biSize + (i * paletteStride);
      if (at + 2 >= dib.Length)
        break;

      palette[(i * 3) + 0] = dib[at + 2]; // R
      palette[(i * 3) + 1] = dib[at + 1]; // G
      palette[(i * 3) + 2] = dib[at + 0]; // B
    }

    // Both bitmaps are stored bottom-up with rows padded to four bytes.
    var colourStride = ((width * bitCount) + 31) / 32 * 4;
    var maskStride = (width + 31) / 32 * 4;
    if (pixelOffset + (colourStride * height) > dib.Length)
      throw new InvalidDataException("ICO entry is truncated: its colour bitmap runs past the end of the entry.");

    var maskOffset = pixelOffset + (colourStride * height);
    var hasMask = maskOffset + (maskStride * height) <= dib.Length;

    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y) {
      var row = dib.AsSpan(pixelOffset + ((height - 1 - y) * colourStride), colourStride);
      var target = y * width * 4;
      for (var x = 0; x < width; ++x) {
        var (b, g, r, a) = _ReadPixel(row, x, bitCount, palette, paletteEntries);
        pixels[target + (x * 4) + 0] = b;
        pixels[target + (x * 4) + 1] = g;
        pixels[target + (x * 4) + 2] = r;
        pixels[target + (x * 4) + 3] = a;
      }
    }

    // Below 32 bits the mask is the only source of transparency; at 32 it is the fallback for the
    // icons whose alpha channel was left at zero.
    var trustMask = hasMask && (bitCount < 32 || _AlphaIsEmpty(pixels));
    if (trustMask)
      for (var y = 0; y < height; ++y) {
        var row = dib.AsSpan(maskOffset + ((height - 1 - y) * maskStride), maskStride);
        for (var x = 0; x < width; ++x) {
          var masked = (row[x >> 3] >> (7 - (x & 7)) & 1) != 0;
          pixels[(((y * width) + x) * 4) + 3] = masked ? (byte)0 : (byte)255;
        }
      }
    else if (bitCount < 32)
      for (var i = 3; i < pixels.Length; i += 4)
        pixels[i] = 255;

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Bgra32,
      PixelData = pixels,
    };
  }

  /// <summary>Whether every pixel came out fully transparent, which no real icon means.</summary>
  private static bool _AlphaIsEmpty(byte[] bgra) {
    for (var i = 3; i < bgra.Length; i += 4)
      if (bgra[i] != 0)
        return false;

    return true;
  }

  /// <summary>One pixel of a DIB row, whatever width its samples are packed at.</summary>
  private static (byte B, byte G, byte R, byte A) _ReadPixel(
    ReadOnlySpan<byte> row, int x, int bitCount, byte[] palette, int paletteEntries) {
    switch (bitCount) {
      case 1:
      case 2:
      case 4: {
        var perByte = 8 / bitCount;
        var index = row[x / perByte] >> ((perByte - 1 - (x % perByte)) * bitCount) & ((1 << bitCount) - 1);
        return _FromPalette(palette, paletteEntries, index);
      }
      case 8:
        return _FromPalette(palette, paletteEntries, row[x]);
      case 16: {
        // RGB555, the layout every 16-bit icon in the wild uses.
        var value = BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]);
        var r5 = (value >> 10) & 0x1F;
        var g5 = (value >> 5) & 0x1F;
        var b5 = value & 0x1F;
        return ((byte)((b5 << 3) | (b5 >> 2)), (byte)((g5 << 3) | (g5 >> 2)), (byte)((r5 << 3) | (r5 >> 2)), (byte)255);
      }
      case 24:
        return (row[x * 3], row[(x * 3) + 1], row[(x * 3) + 2], 255);
      default:
        return (row[x * 4], row[(x * 4) + 1], row[(x * 4) + 2], row[(x * 4) + 3]);
    }
  }

  private static (byte B, byte G, byte R, byte A) _FromPalette(byte[] palette, int entries, int index) {
    if (index < 0 || index >= entries || (index * 3) + 2 >= palette.Length)
      return (0, 0, 0, 255);

    return (palette[(index * 3) + 2], palette[(index * 3) + 1], palette[index * 3], 255);
  }
}
