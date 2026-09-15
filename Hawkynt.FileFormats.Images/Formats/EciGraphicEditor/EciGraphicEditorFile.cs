using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.EciGraphicEditor;

/// <summary>
/// In-memory representation of an ECI Graphic Editor picture — Extended Colour Interlace for the
/// Commodore 64.
/// </summary>
/// <remarks>
/// Two high-resolution FLI frames shown on alternate television fields, and the eye averages them.
/// That is the whole format, and it is worth stating plainly because this reader spent a long time
/// decoding it as multicolour and never got past four fifths of a picture. It is one bit a pixel,
/// not two: a raster line of a character cell names two colours in its video matrix byte and the
/// bitmap chooses between them.
/// <para/>
/// A file is 32770 bytes: a two-byte load address and two sixteen-kilobyte banks. Each bank holds
/// its bitmap at the start and its eight video matrices at 8192, one matrix for each raster line of
/// a character cell, a whole page apiece. The 192 bytes between the 8000-byte bitmap and the first
/// matrix are address-space padding and carry nothing.
/// <para/>
/// FLI costs the left of the screen: the raster switch cannot be ready before the first three
/// character cells are drawn, so the leftmost 24 pixels of every row are whatever the hardware
/// happened to be showing. They are not part of the picture, which is why it is 296 across and not
/// 320 — the same trim AFLI takes, and what the reference decoder draws.
/// <para/>
/// Blending two frames of sixteen colours apiece yields 135 distinct colours rather than 16 or 256:
/// the pairs are unordered, and several of them average to the same value.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png, ConformanceOracle.IrfanView)]
public readonly record struct EciGraphicEditorFile
  : IImageFormatReader<EciGraphicEditorFile>, IImageToRawImage<EciGraphicEditorFile>,
    IImageFromRawImage<EciGraphicEditorFile>, IImageFormatWriter<EciGraphicEditorFile> {

  static string IImageFormatMetadata<EciGraphicEditorFile>.PrimaryExtension => ".eci";
  static string[] IImageFormatMetadata<EciGraphicEditorFile>.FileExtensions => [".eci", ".ecp"];
  static EciGraphicEditorFile IImageFormatReader<EciGraphicEditorFile>.FromSpan(ReadOnlySpan<byte> data) => EciGraphicEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<EciGraphicEditorFile>.ToBytes(EciGraphicEditorFile file) => EciGraphicEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<EciGraphicEditorFile>.VideoModes => [
    new("ECI", [(VisibleWidth, FixedHeight)], [BlendedColorCount])
  ];

  /// <summary>The pixels a stored row holds, the first three cells of which are not part of the picture.</summary>
  public const int FixedWidth = 320;

  /// <summary>Pixels across the picture, the hardware being unable to colour the first 24 of a row.</summary>
  public const int VisibleWidth = 296;

  /// <summary>Where the picture starts within a stored row.</summary>
  internal const int HiddenColumns = FixedWidth - VisibleWidth;

  /// <summary>Rows.</summary>
  public const int FixedHeight = 200;

  /// <summary>Character cells across a stored row.</summary>
  internal const int StoredColumns = FixedWidth / 8;

  /// <summary>How many colours two blended frames of sixteen can show between them.</summary>
  /// <remarks>
  /// Not 256 and not 136. The blend is an average, so a pair and its reverse give the same colour,
  /// and one further coincidence collapses two of the remaining 136 onto each other.
  /// </remarks>
  public const int BlendedColorCount = 135;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of one frame's bitmap.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>How many video matrices a frame carries, one for each raster line of a cell.</summary>
  internal const int ScreenCount = 8;

  /// <summary>The bytes one video matrix takes: a whole page for the thousand it uses.</summary>
  internal const int ScreenStride = 1024;

  /// <summary>All eight video matrices of one frame.</summary>
  internal const int ScreensSize = ScreenCount * ScreenStride;

  /// <summary>Where a frame's video matrices sit inside its bank.</summary>
  internal const int ScreensOffsetInBank = 8192;

  /// <summary>One frame's worth of address space: bitmap, padding, then the matrices.</summary>
  internal const int BankSize = ScreensOffsetInBank + ScreensSize;

  /// <summary>Everything after the load address: the two banks.</summary>
  internal const int PayloadSize = 2 * BankSize;

  /// <summary>The size of an unpacked file.</summary>
  public const int FileSize = LoadAddressSize + PayloadSize;

  /// <summary>Where the second frame's bank starts within the payload.</summary>
  internal const int SecondBankOffset = BankSize;

  /// <summary>
  /// Default load address, putting the first frame in the second VIC bank and the second in the
  /// third.
  /// </summary>
  /// <remarks>
  /// Derived from the layout rather than read off a file. A bitmap must sit at the start or the
  /// middle of a sixteen-kilobyte VIC bank and this one sits at the start, so each frame's bank is
  /// 16K-aligned; loading at $4000 puts the bitmaps at $4000 and $8000 with their matrices at $6000
  /// and $A000, and leaves BASIC and the KERNAL where they are. The reference decoder never reads
  /// these two bytes, so nothing here can check them against it.
  /// </remarks>
  internal const ushort DefaultLoadAddress = 0x4000;

  /// <summary>Image width, always 296.</summary>
  public int Width => VisibleWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The first frame's bitmap, eight thousand bytes, a cell at a time.</summary>
  public byte[] FirstBitmap { get; init; }

  /// <summary>The first frame's eight video matrices, one after another, a whole page apiece.</summary>
  public byte[] FirstScreens { get; init; }

  /// <summary>The second frame's bitmap.</summary>
  public byte[] SecondBitmap { get; init; }

  /// <summary>The second frame's eight video matrices.</summary>
  public byte[] SecondScreens { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// Each frame is drawn on its own and the two are averaged, which is what a display alternating
  /// between them looks like. The result is not an indexed picture: a blend of two palette entries
  /// is usually neither of them.
  /// </remarks>
  public static RawImage ToRawImage(EciGraphicEditorFile file) {
    var first = _DrawFrame(file.FirstBitmap ?? [], file.FirstScreens ?? []);
    var second = _DrawFrame(file.SecondBitmap ?? [], file.SecondScreens ?? []);

    return new() {
      Width = VisibleWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  /// <summary>Draws one high-resolution FLI frame, trimmed to the columns the hardware can colour.</summary>
  private static byte[] _DrawFrame(byte[] bitmap, byte[] screens) {
    var rgb = new byte[VisibleWidth * FixedHeight * 3];

    for (var y = 0; y < FixedHeight; ++y)
    for (var x = 0; x < VisibleWidth; ++x) {
      var column = x + HiddenColumns;
      var cell = y / Commodore64Graphics.CellHeight * StoredColumns + column / 8;

      var pattern = _At(bitmap, cell * Commodore64Graphics.CellHeight + y % Commodore64Graphics.CellHeight);
      var lit = ((pattern >> (7 - column % 8)) & 1) != 0;

      // Which of the eight matrices speaks for this row is the whole of what FLI is.
      var entry = _At(screens, y % ScreenCount * ScreenStride + cell);
      var color = Commodore64Graphics.HexColors[lit ? entry >> 4 : entry & 0x0F];

      var at = (y * VisibleWidth + x) * 3;
      rgb[at] = (byte)(color >> 16);
      rgb[at + 1] = (byte)(color >> 8);
      rgb[at + 2] = (byte)color;
    }

    return rgb;
  }

  /// <summary>A byte from a section a truncated file may not have reached.</summary>
  private static byte _At(byte[] data, int offset) => offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Encodes a picture as ECI, scaling it to 296x200 and approximating what it cannot hold.</summary>
  /// <remarks>
  /// This is the registry's path, so it takes any picture and always produces a file. Colours the
  /// format cannot show are replaced by the nearest it can, and a picture of another size is
  /// resampled. Use <see cref="FromRawImageExact"/> where an approximation is not acceptable: it
  /// refuses by name instead.
  /// </remarks>
  public static EciGraphicEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return _Encode(image.SampleTo(VisibleWidth, FixedHeight).PixelData, exact: false);
  }

  /// <summary>Encodes a picture as ECI, refusing anything the format cannot show pixel for pixel.</summary>
  /// <remarks>
  /// A group of eight pixels on one raster line is described by two video matrix bytes, one per
  /// frame, naming two colours each. Every pixel of the group is then the average of one of the
  /// first pair with one of the second, so the group can hold four colours and only those four
  /// —  and which four is not free, because they are the corners of a two-by-two grid of blends.
  /// A picture that does not sit inside that grid everywhere cannot be written exactly, and this
  /// says so rather than drawing something else.
  /// </remarks>
  /// <exception cref="ArgumentException">The picture is not exactly 296 by 200.</exception>
  /// <exception cref="NotSupportedException">A group of eight pixels needs colours the format cannot show.</exception>
  public static EciGraphicEditorFile FromRawImageExact(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != VisibleWidth || image.Height != FixedHeight)
      throw new ArgumentException(
        $"ECI Graphic Editor pictures are exactly {VisibleWidth} by {FixedHeight}; this one is "
        + $"{image.Width} by {image.Height}.", nameof(image));

    return _Encode(image.EnsureFormat(PixelFormat.Rgb24).PixelData, exact: true);
  }

  private static EciGraphicEditorFile _Encode(byte[] rgb, bool exact) {
    var firstBitmap = new byte[BitmapSize];
    var firstScreens = new byte[ScreensSize];
    var secondBitmap = new byte[BitmapSize];
    var secondScreens = new byte[ScreensSize];

    Span<int> targets = stackalloc int[8];
    Span<int> distinct = stackalloc int[8];
    Span<int> first = stackalloc int[2];
    Span<int> second = stackalloc int[2];
    Span<int> errors = stackalloc int[8 * 256];

    for (var y = 0; y < FixedHeight; ++y)
    for (var left = 0; left < VisibleWidth; left += 8) {
      for (var x = 0; x < 8; ++x) {
        var at = ((y * VisibleWidth) + left + x) * 3;
        targets[x] = (rgb[at] << 16) | (rgb[at + 1] << 8) | rgb[at + 2];
      }

      if (!_TryFitExactly(targets, distinct, first, second)) {
        if (exact)
          throw new NotSupportedException(
            $"ECI Graphic Editor cannot hold this picture exactly: the eight pixels at ({left}, {y}) "
            + "need more colours than one pair of blended VIC-II colour pairs can name, or a colour "
            + "that is not the average of two of the machine's sixteen.");

        _FitApproximately(targets, errors, first, second);
      }

      var column = left + HiddenColumns;
      var cell = y / Commodore64Graphics.CellHeight * StoredColumns + column / 8;
      var at2 = cell * Commodore64Graphics.CellHeight + y % Commodore64Graphics.CellHeight;

      int firstRow = 0, secondRow = 0;
      for (var x = 0; x < 8; ++x) {
        var (a, b) = _Nearest(targets[x], first, second);
        if (a != 0)
          firstRow |= 1 << (7 - x);
        if (b != 0)
          secondRow |= 1 << (7 - x);
      }

      firstBitmap[at2] = (byte)firstRow;
      secondBitmap[at2] = (byte)secondRow;
      firstScreens[y % ScreenCount * ScreenStride + cell] = (byte)((first[1] << 4) | first[0]);
      secondScreens[y % ScreenCount * ScreenStride + cell] = (byte)((second[1] << 4) | second[0]);
    }

    return new() {
      LoadAddress = DefaultLoadAddress,
      FirstBitmap = firstBitmap,
      FirstScreens = firstScreens,
      SecondBitmap = secondBitmap,
      SecondScreens = secondScreens,
    };
  }

  /// <summary>Which entry of each pair describes a colour best; the two bitmap bits for one pixel.</summary>
  private static (int First, int Second) _Nearest(int target, ReadOnlySpan<int> first, ReadOnlySpan<int> second) {
    int bestFirst = 0, bestSecond = 0;
    var bestError = int.MaxValue;

    for (var a = 0; a < 2; ++a)
    for (var b = 0; b < 2; ++b) {
      var error = _Distance(target, _BlendTable[first[a] * Commodore64Graphics.ColorCount + second[b]]);
      if (error >= bestError)
        continue;

      bestError = error;
      bestFirst = a;
      bestSecond = b;
    }

    return (bestFirst, bestSecond);
  }

  /// <summary>
  /// The two colour pairs that show a group of eight pixels with no error at all, if any do.
  /// </summary>
  /// <remarks>
  /// A group holding more than four distinct colours cannot fit, and neither can one holding a
  /// colour that is not a blend, so both are turned down before the search starts. What is left is
  /// a small backtrack: every colour names at most three pairs of VIC-II entries that average to
  /// it, and each pair puts one entry in each frame, so the two sets grow to two apiece or the
  /// branch dies.
  /// </remarks>
  private static bool _TryFitExactly(
    ReadOnlySpan<int> targets, Span<int> distinct, Span<int> first, Span<int> second) {
    var count = 0;
    foreach (var target in targets) {
      var seen = false;
      for (var i = 0; i < count && !seen; ++i)
        seen = distinct[i] == target;

      if (seen)
        continue;

      if (count == 4 || !_BlendSources.ContainsKey(target))
        return false;

      distinct[count++] = target;
    }

    return _Fit(distinct[..count], 0, first, 0, second, 0);
  }

  /// <summary>Assigns the remaining colours to the two frames, or reports that they do not fit.</summary>
  private static bool _Fit(
    ReadOnlySpan<int> distinct, int index, Span<int> first, int firstCount, Span<int> second, int secondCount) {
    if (index == distinct.Length) {
      // A frame that named only one colour says the same thing twice, which is what an unused
      // nibble of a video matrix byte is anyway.
      first[1] = firstCount < 2 ? first[0] : first[1];
      second[1] = secondCount < 2 ? second[0] : second[1];
      return true;
    }

    foreach (var (a, b) in _BlendSources[distinct[index]]) {
      var grownFirst = _Add(first, firstCount, a);
      if (grownFirst < 0)
        continue;

      var grownSecond = _Add(second, secondCount, b);
      if (grownSecond < 0)
        continue;

      if (_Fit(distinct, index + 1, first, grownFirst, second, grownSecond))
        return true;
    }

    return false;
  }

  /// <summary>Adds a colour to a pair, giving the new size, or -1 where the pair is already full.</summary>
  /// <remarks>
  /// A write past <paramref name="count"/> is scratch: the caller keeps its own count, so a branch
  /// that fails leaves nothing behind for the next one to trip over.
  /// </remarks>
  private static int _Add(Span<int> pair, int count, int color) {
    for (var i = 0; i < count; ++i)
      if (pair[i] == color)
        return count;

    if (count == 2)
      return -1;

    pair[count] = color;
    return count + 1;
  }

  /// <summary>
  /// The two colour pairs that show a group of eight pixels with the least error, where none shows
  /// it exactly.
  /// </summary>
  /// <remarks>
  /// The two frames are optimised in turn: with one pair fixed the other is chosen by trying all
  /// 136, which is exact for that half, and the two halves are swapped until neither moves. It
  /// starts from the pair a single non-interlaced frame would use and copies it into both frames —
  /// a colour blended with itself is itself, so the first step is already as good as AFLI and every
  /// step after it is an improvement.
  /// </remarks>
  private static void _FitApproximately(
    ReadOnlySpan<int> targets, Span<int> errors, Span<int> first, Span<int> second) {
    for (var x = 0; x < 8; ++x)
    for (var i = 0; i < 256; ++i)
      errors[x * 256 + i] = _Distance(targets[x], _BlendTable[i]);

    _ChooseUnblended(errors, first);
    second[0] = first[0];
    second[1] = first[1];

    for (var round = 0; round < 3; ++round) {
      _Choose(errors, first, second, forFirstFrame: false);
      _Choose(errors, second, first, forFirstFrame: true);
    }
  }

  /// <summary>The pair a single frame would use, which both frames start from.</summary>
  /// <remarks>
  /// A colour blended with itself is itself, so the diagonal of the blend table is the machine's
  /// own sixteen colours and this is the ordinary high-resolution pair search over them.
  /// </remarks>
  private static void _ChooseUnblended(ReadOnlySpan<int> errors, Span<int> pair) {
    var bestError = long.MaxValue;
    int bestLow = 0, bestHigh = 0;

    for (var high = 0; high < Commodore64Graphics.ColorCount; ++high)
    for (var low = 0; low <= high; ++low) {
      long error = 0;
      for (var x = 0; x < 8; ++x) {
        var slice = errors.Slice(x * 256, 256);
        error += Math.Min(
          slice[high * Commodore64Graphics.ColorCount + high],
          slice[low * Commodore64Graphics.ColorCount + low]);
      }

      if (error >= bestError)
        continue;

      bestError = error;
      bestHigh = high;
      bestLow = low;
    }

    pair[0] = bestLow;
    pair[1] = bestHigh;
  }

  /// <summary>The best pair for one frame, the other frame's pair being settled.</summary>
  private static void _Choose(ReadOnlySpan<int> errors, ReadOnlySpan<int> other, Span<int> pair, bool forFirstFrame) {
    var bestError = long.MaxValue;
    int bestLow = 0, bestHigh = 0;

    for (var high = 0; high < Commodore64Graphics.ColorCount; ++high)
    for (var low = 0; low <= high; ++low) {
      long error = 0;
      for (var x = 0; x < 8; ++x) {
        var slice = errors.Slice(x * 256, 256);
        var best = int.MaxValue;
        for (var i = 0; i < 2; ++i) {
          var withHigh = forFirstFrame ? slice[high * Commodore64Graphics.ColorCount + other[i]] : slice[other[i] * Commodore64Graphics.ColorCount + high];
          var withLow = forFirstFrame ? slice[low * Commodore64Graphics.ColorCount + other[i]] : slice[other[i] * Commodore64Graphics.ColorCount + low];
          best = Math.Min(best, Math.Min(withHigh, withLow));
        }

        error += best;
      }

      if (error >= bestError)
        continue;

      bestError = error;
      bestHigh = high;
      bestLow = low;
    }

    pair[0] = bestLow;
    pair[1] = bestHigh;
  }

  /// <summary>Squared distance in RGB between two packed colours.</summary>
  private static int _Distance(int left, int right) {
    int dr = ((left >> 16) & 0xFF) - ((right >> 16) & 0xFF);
    int dg = ((left >> 8) & 0xFF) - ((right >> 8) & 0xFF);
    int db = (left & 0xFF) - (right & 0xFF);

    return dr * dr + dg * dg + db * db;
  }

  /// <summary>Every pair of the machine's colours, averaged as the display averages them.</summary>
  private static readonly int[] _BlendTable = _BuildBlendTable();

  /// <summary>For each colour a blend can produce, the pairs of entries that produce it.</summary>
  private static readonly Dictionary<int, (int First, int Second)[]> _BlendSources = _BuildBlendSources();

  private static int[] _BuildBlendTable() {
    var table = new int[Commodore64Graphics.ColorCount * Commodore64Graphics.ColorCount];
    for (var a = 0; a < Commodore64Graphics.ColorCount; ++a)
    for (var b = 0; b < Commodore64Graphics.ColorCount; ++b) {
      int first = Commodore64Graphics.HexColors[a], second = Commodore64Graphics.HexColors[b];
      // The same rounding-down average the display makes and the blend helper applies per channel.
      table[a * Commodore64Graphics.ColorCount + b] = (first & second) + (((first ^ second) >> 1) & 0x7F7F7F);
    }

    return table;
  }

  private static Dictionary<int, (int First, int Second)[]> _BuildBlendSources() {
    var sources = new Dictionary<int, List<(int, int)>>();
    for (var a = 0; a < Commodore64Graphics.ColorCount; ++a)
    for (var b = 0; b < Commodore64Graphics.ColorCount; ++b) {
      var color = _BlendTable[a * Commodore64Graphics.ColorCount + b];
      if (!sources.TryGetValue(color, out var pairs))
        sources[color] = pairs = [];

      pairs.Add((a, b));
    }

    var result = new Dictionary<int, (int, int)[]>(sources.Count);
    foreach (var (color, pairs) in sources)
      result[color] = pairs.ToArray();

    return result;
  }
}
