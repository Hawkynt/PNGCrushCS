using System;
using FileFormat.Core;

namespace FileFormat.InterlaceHiresEditor;

/// <summary>In-memory representation of an Interlace Hires Editor picture for the Commodore 64.</summary>
/// <remarks>
/// This wanted 18002 bytes laid out as bitmap, video matrix, bitmap, video matrix. There is no video
/// matrix. A file is a load address and two bitmaps, the first taking a whole eight-kilobyte page for
/// the 8000 bytes it uses, which is 2 + 8192 + 8000 = 16194 — the length of the only sample, and
/// 1808 bytes short of what the reader insisted on, so it was refused.
/// <para/>
/// The two bitmaps are shown one after the other, fast enough that the eye adds them. A pixel is
/// therefore not on or off but on in neither frame, one of them, or both — three levels rather than
/// two, which is exactly the three colours the reference tool draws.
/// <para/>
/// Nothing in the file says what those levels look like. There is no palette, no colour memory and
/// no register: the 192 bytes between the bitmaps are the page padding and are all nought. So the
/// picture is greyscale by construction, and the three values used here are the ones RECOIL draws so
/// that the two agree — they are the reference's choice rather than the file's, because the file
/// does not have one.
/// </remarks>
public readonly record struct InterlaceHiresEditorFile
  : IImageFormatReader<InterlaceHiresEditorFile>, IImageToRawImage<InterlaceHiresEditorFile>,
    IImageFromRawImage<InterlaceHiresEditorFile>, IImageFormatWriter<InterlaceHiresEditorFile> {

  static string IImageFormatMetadata<InterlaceHiresEditorFile>.PrimaryExtension => ".ihe";
  static string[] IImageFormatMetadata<InterlaceHiresEditorFile>.FileExtensions => [".ihe"];
  static InterlaceHiresEditorFile IImageFormatReader<InterlaceHiresEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => InterlaceHiresEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<InterlaceHiresEditorFile>.ToBytes(InterlaceHiresEditorFile file)
    => InterlaceHiresEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<InterlaceHiresEditorFile>.VideoModes => [
    new("Interlace Hires", [(FixedWidth, FixedHeight)], [3])
  ];

  /// <summary>Pixels across.</summary>
  public const int FixedWidth = 320;

  /// <summary>Rows.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>The bytes a bitmap uses.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>The address space a bitmap occupies, being a whole eight-kilobyte page.</summary>
  internal const int BitmapStride = 8192;

  /// <summary>Where the first bitmap starts.</summary>
  internal const int FirstBitmapOffset = LoadAddressSize;

  /// <summary>Where the second starts: a whole page after the first, not 8000 bytes after it.</summary>
  internal const int SecondBitmapOffset = LoadAddressSize + BitmapStride;

  /// <summary>The whole of a file: 2 + 8192 + 8000.</summary>
  public const int ExpectedFileSize = SecondBitmapOffset + BitmapSize;

  /// <summary>Default load address, putting the first bitmap at the start of a 16K bank.</summary>
  internal const ushort DefaultLoadAddress = 0x4000;

  /// <summary>
  /// The three levels a pixel can be shown at, darkest first.
  /// </summary>
  /// <remarks>
  /// Lit in both frames is darkest, in neither lightest, and the middle is half of the difference —
  /// which is what makes them a scale rather than three unrelated colours.
  /// </remarks>
  internal static ReadOnlySpan<byte> Levels => [0, 0, 0, 54, 54, 54, 108, 108, 108];

  /// <summary>Always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The first frame's bitmap.</summary>
  public byte[] FirstBitmap { get; init; }

  /// <summary>The second frame's bitmap.</summary>
  public byte[] SecondBitmap { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(InterlaceHiresEditorFile file) {
    var first = file.FirstBitmap ?? [];
    var second = file.SecondBitmap ?? [];
    var indices = new byte[FixedWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; ++x) {
        var cell = y / 8 * (FixedWidth / 8) + x / 8;
        var bit = 7 - x % 8;
        var lit = ((first[cell * 8 + y % 8] >> bit) & 1) + ((second[cell * 8 + y % 8] >> bit) & 1);

        // Lit in both frames is the darkest of the three, so the count counts down the scale.
        indices[y * FixedWidth + x] = (byte)(2 - lit);
      }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = Levels.ToArray(),
      PaletteCount = 3,
    };
  }

  /// <summary>Encodes a picture as an Interlace Hires pair, scaling it to 320x200 first.</summary>
  /// <remarks>
  /// The two frames give a pixel three levels, not two, and which frames are lit is what picks
  /// between them: both is darkest, neither lightest. So a grey is rounded to one of the three and
  /// then turned back into a count of lit frames, and the count is spent on the first frame before
  /// the second — the two frames being interchangeable here, since only how many are lit is read.
  /// </remarks>
  public static InterlaceHiresEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight).PixelData;
    var first = new byte[BitmapSize];
    var second = new byte[BitmapSize];

    for (var y = 0; y < FixedHeight; ++y)
    for (var x = 0; x < FixedWidth; ++x) {
      var at = (y * FixedWidth + x) * 3;
      var luminance = (rgb[at] * 77 + rgb[at + 1] * 151 + rgb[at + 2] * 28) >> 8;

      // The three levels are 0, 54 and 108, so anything above the top one belongs to it.
      var level = Math.Min(2, (luminance + 27) / 54);
      var lit = 2 - level;

      var atByte = (y / 8 * (FixedWidth / 8) + x / 8) * 8 + y % 8;
      var mask = (byte)(0x80 >> (x % 8));
      if (lit >= 1)
        first[atByte] |= mask;
      if (lit == 2)
        second[atByte] |= mask;
    }

    return new() { LoadAddress = DefaultLoadAddress, FirstBitmap = first, SecondBitmap = second };
  }
}
