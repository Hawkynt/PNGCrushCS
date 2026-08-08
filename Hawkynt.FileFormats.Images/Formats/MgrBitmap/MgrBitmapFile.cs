using System;
using FileFormat.Core;

namespace FileFormat.MgrBitmap;

/// <summary>In-memory representation of an MGR (MGR Window Manager) bitmap image.</summary>
public readonly record struct MgrBitmapFile : IImageFormatReader<MgrBitmapFile>, IImageToRawImage<MgrBitmapFile>, IImageFromRawImage<MgrBitmapFile>, IImageFormatWriter<MgrBitmapFile> {

  static string IImageFormatMetadata<MgrBitmapFile>.PrimaryExtension => ".mgr";
  static string[] IImageFormatMetadata<MgrBitmapFile>.FileExtensions => [".mgr"];
  static MgrBitmapFile IImageFormatReader<MgrBitmapFile>.FromSpan(ReadOnlySpan<byte> data) => MgrBitmapReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MgrBitmapFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<MgrBitmapFile>.ToBytes(MgrBitmapFile file) => MgrBitmapWriter.ToBytes(file);

  /// <summary>Bytes of header in the older form: the letters and the two dimensions.</summary>
  /// <remarks>
  /// The one real sample is 518 bytes, opens <c>zz</c>, and states 64 by 64 — which is six bytes and
  /// then 512 of bitmap, exactly its length. Read with the longer header it is two bytes short, and
  /// read with the letters this demanded it was refused outright.
  /// </remarks>
  public const int ShortHeaderSize = 6;

  /// <summary>Bytes of header in the longer form, which states a depth as well.</summary>
  public const int HeaderSize = 8;

  /// <summary>What each six-bit half is biased by, to keep the header typable.</summary>
  public const byte HeaderBias = 0x20;

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Whether the file this came from carried the longer header, which states a depth.</summary>
  /// <remarks>Kept so a file read and written again comes back the length it went in as.</remarks>
  public bool HasDepthByte { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Paper first: a set bit is the mark, which the tool that reads these draws black.</summary>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(MgrBitmapFile file) {
    // A row is padded out to a whole byte, so the bits are not one unbroken stream and cannot be
    // handed over as such — at any width that is not a multiple of eight the padding would be read
    // as picture and every row after the first would start in the wrong place.
    var stride = (file.Width + 7) / 8;
    var data = file.PixelData ?? [];
    var pixels = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      var at = y * stride + (x >> 3);
      pixels[y * file.Width + x] = (byte)(at < data.Length ? (data[at] >> (~x & 7)) & 1 : 0);
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Builds a bitmap from a picture, at whatever size the picture already is.</summary>
  /// <remarks>
  /// The header carries the dimensions, so unlike most of the machine formats here there is nothing
  /// to sample to — the picture keeps its own size and only its colours are reduced.
  /// <para/>
  /// A set bit is the dark one, matching what the reader draws. Sampling the other way round — the
  /// default, which suits a machine drawing light ink over a dark screen — wrote every picture as
  /// its own negative, and a black pixel came back white.
  /// </remarks>
  public static MgrBitmapFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var set = GlyphSheet.Sample(image, image.Width, image.Height, setWhenBright: false);
    var stride = (image.Width + 7) / 8;
    var bitmap = new byte[stride * image.Height];

    for (var y = 0; y < image.Height; ++y)
    for (var x = 0; x < image.Width; ++x) {
      if (!set[y * image.Width + x])
        continue;

      bitmap[y * stride + (x >> 3)] |= (byte)(1 << (~x & 7));
    }

    return new() { Width = image.Width, Height = image.Height, PixelData = bitmap };
  }

}
