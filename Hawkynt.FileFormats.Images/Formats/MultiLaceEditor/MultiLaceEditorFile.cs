using System;
using FileFormat.Core;

namespace FileFormat.MultiLaceEditor;

/// <summary>In-memory representation of a Multi-Lace Editor logo for the Commodore 64.</summary>
/// <remarks>
/// A logo editor rather than a picture editor, and the shape follows from that: 320 by 56, two
/// four-colour fields shown alternately, and no colour memory or video matrix at all. The four
/// colours are fixed in the program — black, brown, orange and green — so a field is nothing but two
/// bits a pixel, 2048 bytes of it, which covers 256 character cells of the 280 the seven rows hold.
/// The 24 that are left, at the bottom right, are always black.
/// <para/>
/// The second field is displaced one pixel to the left, which is what lets the pair resolve edges
/// finer than either field can place them, and it comes first in the file.
/// <para/>
/// What was written instead was 19003 bytes of two 160 by 200 multicolour screens with video
/// matrices and a shared colour memory — the shape of a Koala picture doubled, which is not this
/// format in size, geometry or colour.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct MultiLaceEditorFile
  : IImageFormatReader<MultiLaceEditorFile>, IImageToRawImage<MultiLaceEditorFile>,
    IImageFromRawImage<MultiLaceEditorFile>, IImageFormatWriter<MultiLaceEditorFile> {

  static string IImageFormatMetadata<MultiLaceEditorFile>.PrimaryExtension => ".mle";
  static string[] IImageFormatMetadata<MultiLaceEditorFile>.FileExtensions => [".mle"];
  static MultiLaceEditorFile IImageFormatReader<MultiLaceEditorFile>.FromSpan(ReadOnlySpan<byte> data) => MultiLaceEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<MultiLaceEditorFile>.ToBytes(MultiLaceEditorFile file) => MultiLaceEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MultiLaceEditorFile>.VideoModes => [
    new("Multi-Lace Editor", [(FixedWidth, FixedHeight)], [ColorCount])
  ];

  /// <summary>The fixed width of the logo in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the logo in pixels.</summary>
  public const int FixedHeight = 56;

  /// <summary>Colours a field can show, and the program's own choice of which.</summary>
  public const int ColorCount = 4;

  /// <summary>
  /// The four machine colours the editor draws in: black, brown, orange and green.
  /// </summary>
  /// <remarks>
  /// Not a palette the file stores — the program has these and no others, so the two bits a pixel
  /// name one of these four directly.
  /// </remarks>
  internal static ReadOnlySpan<byte> ColorIndices => [0, 9, 8, 5];

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Bytes one field takes: 256 character cells of eight.</summary>
  internal const int FieldSize = 2048;

  /// <summary>Where the displaced field starts, which is the one the file holds first.</summary>
  internal const int SecondFieldOffset = LoadAddressSize;

  /// <summary>Where the field drawn where it says starts.</summary>
  internal const int FirstFieldOffset = SecondFieldOffset + FieldSize;

  /// <summary>How far left the second field sits.</summary>
  internal const int SecondFieldShift = 1;

  /// <summary>The length of a whole logo, which is also what identifies it.</summary>
  public const int FileSize = FirstFieldOffset + FieldSize;

  /// <summary>Default load address, which puts the first field at $0800.</summary>
  internal const ushort DefaultLoadAddress = 0x0800;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 56.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The field drawn where it says, two bits a pixel.</summary>
  public byte[] FirstField { get; init; }

  /// <summary>The field drawn one pixel to the left.</summary>
  public byte[] SecondField { get; init; }

  /// <summary>Converts this logo to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MultiLaceEditorFile file) {
    var palette = _Palette();
    var first = Commodore64Graphics.DecodeFourColor(
      file.FirstField ?? [], 0, 0, FixedWidth, FixedHeight, palette);
    var second = Commodore64Graphics.DecodeFourColor(
      file.SecondField ?? [], 0, SecondFieldShift, FixedWidth, FixedHeight, palette);

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  /// <summary>Encodes a logo as a Multi-Lace pair, scaling it to 320x56 first.</summary>
  /// <remarks>
  /// Both fields carry the same picture, the second placed one pixel left so that decoding puts it
  /// back where it was: their average is then the picture itself. Choosing two fields whose average
  /// resolves an edge finer than either can place it is what the displacement is for and is a
  /// problem of its own.
  /// </remarks>
  public static MultiLaceEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight).PixelData;
    var indices = new byte[FixedWidth * FixedHeight];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = _Nearest(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);

    return new() {
      LoadAddress = DefaultLoadAddress,
      FirstField = Commodore64Graphics.PackFourColor(indices, 0, 0, FixedWidth, FixedHeight, FieldSize),
      SecondField = Commodore64Graphics.PackFourColor(indices, 0, SecondFieldShift, FixedWidth, FixedHeight, FieldSize),
    };
  }

  /// <summary>The four colours as RGB triplets, which is what the shared decoder wants.</summary>
  private static byte[] _Palette() {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var colour = Commodore64Graphics.HexColors[ColorIndices[i]];
      palette[i * 3] = (byte)(colour >> 16);
      palette[i * 3 + 1] = (byte)(colour >> 8);
      palette[i * 3 + 2] = (byte)colour;
    }

    return palette;
  }

  /// <summary>Which of the four the editor has is nearest a given colour.</summary>
  private static byte _Nearest(byte red, byte green, byte blue) {
    byte best = 0;
    var bestCost = int.MaxValue;

    for (var i = 0; i < ColorCount; ++i) {
      var colour = Commodore64Graphics.HexColors[ColorIndices[i]];
      int dr = ((colour >> 16) & 0xFF) - red, dg = ((colour >> 8) & 0xFF) - green, db = (colour & 0xFF) - blue;
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (byte)i;
    }

    return best;
  }
}
