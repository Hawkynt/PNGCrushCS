using System;
using FileFormat.Core;

namespace FileFormat.Bfli;

/// <summary>A BFLI picture: a multicolour FLI screen run over twice the usual height, 320 by 400.</summary>
/// <remarks>
/// This used to draw 320 by 200 as an ordinary high-resolution screen, one bitmap and one video
/// matrix, which is neither the size nor the mode. BFLI is "big FLI": the raster trick that lets a
/// FLI screen repoint its video matrix on every raster line, run over 400 rows, so the file carries
/// two bitmaps of 8000 bytes and two sets of eight video matrices. RECOIL and XnView both draw it
/// 320 by 400.
/// <para/>
/// The layout comes from XnView's own reader, which was disassembled because nothing published
/// describes the file order, and every field below was then checked against the three samples in the
/// corpus. A file is exactly 33795 bytes: a load address of 0x3BFF little-endian, the letter
/// <c>b</c>, and 33792 bytes of payload. The payload is not the memory image in address order; it is
/// the order the program saved its memory in, and it interleaves:
/// <list type="number">
/// <item>1024 bytes of colour memory;</item>
/// <item>eight blocks of 1000 video matrix entries, each followed by the 24 bytes of the page it does
/// not fill — the first half's matrices, one per raster line of a character cell;</item>
/// <item>8000 bytes, the first half's bitmap;</item>
/// <item>192 bytes, the tail of the page that bitmap stands in;</item>
/// <item>eight more blocks, each of 976 entries, 24 bytes of padding and then 24 entries — the second
/// half's matrices, which begin part-way into their page and so are saved split;</item>
/// <item>7808 bytes and, right at the end of the file, the 192 bytes that come before them: together
/// the second half's bitmap.</item>
/// </list>
/// Put back together the picture is uniform over all 400 rows: the bitmap is 16000 bytes in the
/// machine's cell order, and each of the eight matrices holds 2000 entries rather than 1000, the
/// first thousand serving the top half and the second the bottom.
/// <para/>
/// Colour memory is the exception, and it is hardware rather than a quirk. The machine has 1024 bytes
/// of it and the video chip addresses it with ten bits, so a picture of 2000 character cells wraps:
/// cell 1024 shows the colour of cell 0. Reading it modulo 1000 instead — the number of cells an
/// ordinary screen has — looks more sensible and is wrong, and it shows: the bottom three quarters of
/// every sample then comes out speckled.
/// </remarks>
[FormatMagicBytes([0xFF, 0x3B, 0x62])]
public readonly record struct BfliFile
  : IImageFormatReader<BfliFile>, IImageToRawImage<BfliFile>,
    IImageFromRawImage<BfliFile>, IImageFormatWriter<BfliFile> {

  static string IImageFormatMetadata<BfliFile>.PrimaryExtension => ".bfl";

  /// <summary>
  /// The names XnView's catalogue gives this reader, less the two that belong to something else in
  /// this library: <c>.fli</c> is Autodesk's animation and <c>.afl</c> is AFLI, and both have their
  /// own reader here. <c>.flp</c> is claimed by nothing else and is taken.
  /// </summary>
  static string[] IImageFormatMetadata<BfliFile>.FileExtensions => [".bfl", ".bfli", ".flp"];

  static BfliFile IImageFormatReader<BfliFile>.FromSpan(ReadOnlySpan<byte> data) => BfliReader.FromSpan(data);
  static byte[] IImageFormatWriter<BfliFile>.ToBytes(BfliFile file) => BfliWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<BfliFile>.VideoModes => [
    new("BFLI", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Pixels across, each of the 160 the bitmap stores drawn twice.</summary>
  public const int FixedWidth = 320;

  /// <summary>Rows: twice a FLI screen, which is what makes this one big.</summary>
  public const int FixedHeight = 400;

  /// <summary>Multicolour pixels a row actually holds.</summary>
  internal const int StoredWidth = FixedWidth / 2;

  /// <summary>The load address every file carries.</summary>
  internal const ushort LoadAddress = 0x3BFF;

  /// <summary>The byte standing at the load address, which says which of the FLI family this is.</summary>
  internal const byte Marker = (byte)'b';

  /// <summary>The load address and the marker behind it.</summary>
  internal const int HeaderSize = 3;

  /// <summary>Payload bytes, which is the whole of the memory the program saved.</summary>
  internal const int PayloadSize = 0x8400;

  /// <summary>The only length a BFLI file has.</summary>
  public const int FileSize = HeaderSize + PayloadSize;

  /// <summary>Character cells across.</summary>
  internal const int Columns = 40;

  /// <summary>Raster lines down one character cell.</summary>
  internal const int CellHeight = 8;

  /// <summary>Character cells the whole 400 rows hold.</summary>
  internal const int CellCount = Columns * FixedHeight / CellHeight;

  /// <summary>The bitmap once both halves are put together, eight bytes to a cell.</summary>
  internal const int BitmapSize = CellCount * CellHeight;

  /// <summary>Video matrices, one for each raster line of a cell, which is what FLI buys.</summary>
  internal const int ScreenCount = 8;

  /// <summary>Entries one matrix holds: a thousand for each half of the picture.</summary>
  internal const int ScreenEntries = CellCount;

  /// <summary>Colour memory the machine has, and what the video chip's ten address bits reach.</summary>
  internal const int ColorRamSize = 1024;

  /// <summary>Pixel pairs at the left of every row that the picture does not control.</summary>
  /// <remarks>
  /// The raster interrupt that repoints the video matrix cannot have run before the first three
  /// character cells of a row are drawn, so those 24 pixels show whatever the hardware had. XnView
  /// draws them from a fixed table rather than from the file and that is reproduced here; dropping
  /// them instead would give a picture 296 across, which is neither the width the reader states nor
  /// the width either reference tool reports for this format.
  /// </remarks>
  internal const int HiddenPairs = 12;

  /// <summary>What the four bit patterns show in the cells the raster switch has not reached.</summary>
  /// <remarks>
  /// Taken from XnView's table byte for byte. It is what the hardware leaves behind: pattern 00 is
  /// the background register, and 01 and 10 are the two nibbles of a matrix entry fetched as 0xFF off
  /// an idle bus, which is white twice.
  /// </remarks>
  private static ReadOnlySpan<byte> _HiddenColumnColours => [0x00, 0x0F, 0x0F, 0x09];

  /// <summary>The 33792 bytes behind the header, in the order the file stores them.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 400.</summary>
  public int Height => FixedHeight;

  /// <summary>Draws the picture: two bits a pixel, a matrix per raster line, one colour memory.</summary>
  public static RawImage ToRawImage(BfliFile file) {
    if (file.RawData is not { Length: PayloadSize } payload)
      throw new InvalidOperationException($"A BFLI picture holds {PayloadSize} bytes of payload.");

    var bitmap = new byte[BitmapSize];
    var screens = new byte[ScreenCount * ScreenEntries];
    var colorRam = new byte[ColorRamSize];
    _Unpack(payload, bitmap, screens, colorRam);

    var indices = new byte[FixedWidth * FixedHeight];
    for (var y = 0; y < FixedHeight; ++y) {
      // Which of the eight matrices speaks for this raster line is the whole of what FLI is.
      var screen = screens.AsSpan(y % ScreenCount * ScreenEntries, ScreenEntries);
      var band = y / CellHeight;
      var bitmapBase = band * Columns * CellHeight + y % CellHeight;
      var cellBase = band * Columns;

      for (var pair = 0; pair < StoredWidth; ++pair) {
        var pattern = (bitmap[bitmapBase + pair / 4 * CellHeight] >> ((3 - pair % 4) * 2)) & 3;

        int index;
        if (pair < HiddenPairs)
          index = _HiddenColumnColours[pattern];
        else {
          var cell = cellBase + pair / 4;
          index = pattern switch {
            0 => 0,
            1 => screen[cell] >> 4,
            2 => screen[cell] & 0x0F,
            _ => colorRam[cell & (ColorRamSize - 1)] & 0x0F,
          };
        }

        var at = y * FixedWidth + pair * 2;
        indices[at] = indices[at + 1] = (byte)index;
      }
    }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  /// <summary>Encodes a picture as BFLI, scaling it to the screen's size first.</summary>
  /// <remarks>
  /// Colour memory is settled before anything else, because it is what the two halves of the picture
  /// have to share: slot n serves cell n and cell n + 1024 alike, so it is chosen against both at once
  /// rather than by either. Only then does each raster line of each cell pick the two colours its
  /// matrix entry names, which is the freedom FLI has and an ordinary screen has not.
  /// <para/>
  /// The leftmost three cells are encoded like any other and do not come back — nothing stored there
  /// survives, because the hardware has not switched matrices yet when they are drawn — so a round
  /// trip reproduces the picture from column 24 rightwards.
  /// </remarks>
  public static BfliFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(StoredWidth, FixedHeight).PixelData;
    var pixels = new byte[StoredWidth * FixedHeight];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)Commodore64Graphics.FindNearestColorIndex(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);

    var colorRam = new byte[ColorRamSize];
    Span<byte> line = stackalloc byte[4];
    for (var slot = 0; slot < ColorRamSize; ++slot) {
      var best = 0;
      var bestError = long.MaxValue;
      for (var candidate = 0; candidate < Commodore64Graphics.ColorCount; ++candidate) {
        long error = 0;
        for (var cell = slot; cell < CellCount; cell += ColorRamSize)
        for (var row = 0; row < CellHeight; ++row) {
          _ReadLine(pixels, cell, row, line);
          error += _ChooseLinePair(line, candidate, out _, out _);
        }

        if (error >= bestError)
          continue;

        bestError = error;
        best = candidate;
      }

      colorRam[slot] = (byte)best;
    }

    var bitmap = new byte[BitmapSize];
    var screens = new byte[ScreenCount * ScreenEntries];
    for (var cell = 0; cell < CellCount; ++cell) {
      var entry = colorRam[cell & (ColorRamSize - 1)];
      for (var row = 0; row < CellHeight; ++row) {
        _ReadLine(pixels, cell, row, line);
        _ChooseLinePair(line, entry, out var high, out var low);

        var bits = 0;
        for (var x = 0; x < 4; ++x)
          bits |= _Pattern(line[x], entry, high, low) << ((3 - x) * 2);

        bitmap[cell * CellHeight + row] = (byte)bits;
        screens[row * ScreenEntries + cell] = (byte)((high << 4) | low);
      }
    }

    var payload = new byte[PayloadSize];
    _Pack(bitmap, screens, colorRam, payload);

    return new() { RawData = payload };
  }

  /// <summary>The four stored pixels of one raster line of one character cell.</summary>
  private static void _ReadLine(ReadOnlySpan<byte> pixels, int cell, int row, Span<byte> line)
    => pixels.Slice((cell / Columns * CellHeight + row) * StoredWidth + cell % Columns * 4, 4).CopyTo(line);

  /// <summary>
  /// The two colours a raster line's matrix entry is best off naming, given the background and the
  /// colour memory it already has for free, and what that choice costs.
  /// </summary>
  private static long _ChooseLinePair(ReadOnlySpan<byte> line, int colorRam, out int high, out int low) {
    high = low = 0;

    var bestError = long.MaxValue;
    for (var a = 0; a < line.Length; ++a)
    for (var b = a; b < line.Length; ++b) {
      long error = 0;
      foreach (var index in line)
        error += Math.Min(
          Math.Min(_Distance(index, 0), _Distance(index, colorRam)),
          Math.Min(_Distance(index, line[a]), _Distance(index, line[b])));

      if (error >= bestError)
        continue;

      bestError = error;
      high = line[a];
      low = line[b];
    }

    return bestError == long.MaxValue ? 0 : bestError;
  }

  /// <summary>Which of the four colours a cell can show describes a pixel with the least error.</summary>
  private static int _Pattern(int index, int colorRam, int high, int low) {
    var pattern = 0;
    var best = _Distance(index, 0);

    var distance = _Distance(index, high);
    if (distance < best) {
      best = distance;
      pattern = 1;
    }

    distance = _Distance(index, low);
    if (distance < best) {
      best = distance;
      pattern = 2;
    }

    return _Distance(index, colorRam) < best ? 3 : pattern;
  }

  /// <summary>Squared distance in RGB between two of the machine's colours.</summary>
  private static int _Distance(int left, int right) {
    if (left == right)
      return 0;

    int a = Commodore64Graphics.HexColors[left], b = Commodore64Graphics.HexColors[right];
    int dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
    int dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
    int db = (a & 0xFF) - (b & 0xFF);

    return dr * dr + dg * dg + db * db;
  }

  /// <summary>Entries of a video matrix that stand in the file rather than being page padding.</summary>
  private const int _MatrixSize = 1000;

  /// <summary>Where the second half's entries begin within a matrix.</summary>
  private const int _SecondMatrixStart = 1000;

  /// <summary>Entries of the second half's matrix the file keeps back until after its padding.</summary>
  private const int _SecondMatrixSplit = 24;

  /// <summary>Bytes of page padding the file writes after each block and this does not read.</summary>
  private const int _Padding = 24;

  /// <summary>Bytes of a bitmap that fall outside its page and are saved apart from it.</summary>
  private const int _BitmapTail = 192;

  /// <summary>Bytes one half's bitmap holds.</summary>
  private const int _HalfBitmap = BitmapSize / 2;

  /// <summary>Takes the file's payload apart into the bitmap, the matrices and colour memory.</summary>
  private static void _Unpack(ReadOnlySpan<byte> payload, Span<byte> bitmap, Span<byte> screens, Span<byte> colorRam) {
    var at = 0;

    payload.Slice(at, ColorRamSize).CopyTo(colorRam);
    at += ColorRamSize;

    for (var i = 0; i < ScreenCount; ++i) {
      payload.Slice(at, _MatrixSize).CopyTo(screens[(i * ScreenEntries)..]);
      at += _MatrixSize + _Padding;
    }

    payload.Slice(at, _HalfBitmap).CopyTo(bitmap);
    at += _HalfBitmap + _BitmapTail;

    for (var i = 0; i < ScreenCount; ++i) {
      var second = i * ScreenEntries + _SecondMatrixStart;
      payload.Slice(at, _MatrixSize - _SecondMatrixSplit).CopyTo(screens[(second + _SecondMatrixSplit)..]);
      at += _MatrixSize - _SecondMatrixSplit + _Padding;
      payload.Slice(at, _SecondMatrixSplit).CopyTo(screens[second..]);
      at += _SecondMatrixSplit;
    }

    payload.Slice(at, _HalfBitmap - _BitmapTail).CopyTo(bitmap[(_HalfBitmap + _BitmapTail)..]);
    at += _HalfBitmap;

    payload.Slice(at, _BitmapTail).CopyTo(bitmap[_HalfBitmap..]);
  }

  /// <summary>Puts them back in the order the file stores them, the inverse of the above.</summary>
  private static void _Pack(ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> screens, ReadOnlySpan<byte> colorRam, Span<byte> payload) {
    var at = 0;

    colorRam.CopyTo(payload[at..]);
    at += ColorRamSize;

    for (var i = 0; i < ScreenCount; ++i) {
      screens.Slice(i * ScreenEntries, _MatrixSize).CopyTo(payload[at..]);
      at += _MatrixSize + _Padding;
    }

    bitmap[.._HalfBitmap].CopyTo(payload[at..]);
    at += _HalfBitmap + _BitmapTail;

    for (var i = 0; i < ScreenCount; ++i) {
      var second = i * ScreenEntries + _SecondMatrixStart;
      screens.Slice(second + _SecondMatrixSplit, _MatrixSize - _SecondMatrixSplit).CopyTo(payload[at..]);
      at += _MatrixSize - _SecondMatrixSplit + _Padding;
      screens.Slice(second, _SecondMatrixSplit).CopyTo(payload[at..]);
      at += _SecondMatrixSplit;
    }

    bitmap.Slice(_HalfBitmap + _BitmapTail, _HalfBitmap - _BitmapTail).CopyTo(payload[at..]);
    at += _HalfBitmap;

    bitmap.Slice(_HalfBitmap, _BitmapTail).CopyTo(payload[at..]);
  }
}
