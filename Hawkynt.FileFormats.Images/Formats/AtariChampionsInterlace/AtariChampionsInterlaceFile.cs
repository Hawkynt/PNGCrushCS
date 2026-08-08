using System;
using FileFormat.Core;

namespace FileFormat.AtariChampionsInterlace;

/// <summary>In-memory representation of a Champions' Interlace picture (.cin, .cci).</summary>
/// <remarks>
/// Two interlaced fields built a scanline at a time rather than a screen at a time: the Graphics 15
/// rows alternate between the two fields as they are stored, so consecutive bytes in the file are
/// consecutive rows of the picture and not of either field. The Graphics 11 hue rows then fill in
/// what each field is missing.
/// <para/>
/// The longest form gives every scanline its own four colour registers, stored as four planes of
/// 256 bytes so that one register's values down the whole screen are contiguous — which is the
/// order a display routine rewriting one register per line wants to read them in.
/// </remarks>
public readonly record struct AtariChampionsInterlaceFile
  : IImageFormatReader<AtariChampionsInterlaceFile>, IImageToRawImage<AtariChampionsInterlaceFile>,
    IImageFromRawImage<AtariChampionsInterlaceFile>, IImageFormatWriter<AtariChampionsInterlaceFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Bytes one row of one field occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>Bytes a row of hues occupies across both fields.</summary>
  public const int HueStride = Stride * 2;

  /// <summary>Size of a file with no colours of its own.</summary>
  public const int BareSize = 15360;

  /// <summary>Size of a file with one set of registers for the whole picture.</summary>
  public const int OneSetSize = 16004;

  /// <summary>Size of a file with a set of registers per scanline.</summary>
  public const int PerRowSize = 16384;

  /// <summary>The text a compressed file starts with.</summary>
  public const string CompressedSignature = "CIN 1.2 ";

  /// <summary>The registers a file with none of its own falls back to.</summary>
  public static ReadOnlySpan<byte> DefaultRegisters => [0, 4, 8, 12];

  static string IImageFormatMetadata<AtariChampionsInterlaceFile>.PrimaryExtension => ".cin";
  static string[] IImageFormatMetadata<AtariChampionsInterlaceFile>.FileExtensions => [".cin", ".cci"];
  static AtariChampionsInterlaceFile IImageFormatReader<AtariChampionsInterlaceFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariChampionsInterlaceReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariChampionsInterlaceFile>.ToBytes(AtariChampionsInterlaceFile file)
    => AtariChampionsInterlaceWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariChampionsInterlaceFile>.VideoModes => [
    new("Champions' Interlace", [(Width, 192), (Width, 200)], [256])
  ];

  /// <summary>The picture, unpacked if it was compressed.</summary>
  public byte[] Data { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(AtariChampionsInterlaceFile file) {
    var data = file.Data ?? [];
    var height = file.Height;

    var first = new byte[Width * height];
    var second = new byte[Width * height];

    for (var y = 0; y < height; ++y) {
      // A file long enough gives every scanline its own registers, one plane per register.
      var registers = data.Length == PerRowSize
        ? _RowRegisters(data, BareSize + y)
        : data.Length == OneSetSize ? data.AsSpan(OneSetSize - 4, 4).ToArray() : DefaultRegisters.ToArray();

      // Consecutive stored rows belong to alternate fields.
      Atari8BitGraphics.DecodeGr15Into(
        data, y * Stride, Stride, (y & 1) == 0 ? first : second, y * Width, Width, Width, 1, registers);
    }

    Atari8BitGraphics.BlendGr11Into(data, Stride * height + Stride, HueStride, first, Width, height, 1);
    Atari8BitGraphics.BlendGr11Into(data, Stride * height, HueStride, second, Width, height, 0);

    return new() {
      Width = Width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(
        Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }

  /// <summary>
  /// Reads one scanline's registers, which sit 256 bytes apart so that each register's values run
  /// contiguously down the screen.
  /// </summary>
  private static byte[] _RowRegisters(ReadOnlySpan<byte> data, int offset) {
    var registers = new byte[Atari8BitGraphics.Gr15RegisterCount];
    for (var i = 0; i < registers.Length; ++i) {
      var at = offset + i * 256;
      registers[i] = at < data.Length ? data[at] : (byte)0;
    }

    return registers;
  }

  /// <summary>Rows a picture with a set of registers per scanline holds.</summary>
  public const int PerRowHeight = 192;

  /// <summary>Logical pixels a Graphics 15 row holds, each drawn two screen pixels wide.</summary>
  public const int LogicalWidth = Width / 2;

  /// <summary>Hue nibbles a row holds, each covering four screen pixels.</summary>
  public const int HueNibbles = Width / 4;

  /// <summary>Bytes a register plane occupies, one register's value for every scanline.</summary>
  public const int RegisterPlane = 256;

  /// <summary>Passes of improvement over the luminances and the hues.</summary>
  private const int _PASSES = 3;

  /// <summary>
  /// Encodes a picture as two interlaced fields with a set of colour registers per scanline.
  /// </summary>
  /// <remarks>
  /// Written in the form that gives every scanline its own four registers. The other two forms give
  /// the whole picture one set of four, which is what the fields are interlaced to get away from —
  /// four colours down a whole screen is a worse picture than four down each line, and the longer
  /// file is what buys it.
  /// <para/>
  /// A register's hue never reaches the screen: the Graphics 11 rows lay their own hue over every
  /// row they touch, so what the register contributes is its luminance and the low bit of that does
  /// not reach the screen either. Eight luminances, four of them per scanline, is the whole of what
  /// the bitmap says; the colour comes from the hue rows.
  /// <para/>
  /// A hue row averages the luminances above and below it, so a nibble reaches four scanlines and
  /// the luminances and hues are settled against each other rather than one at a time.
  /// </remarks>
  public static AtariChampionsInterlaceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, PerRowHeight).PixelData;
    var gtia = Atari8BitGraphics.Palette;

    // What every pair of colour bytes looks like once the display has averaged the two fields.
    var blend = new byte[256 * 256 * 3];
    for (var first = 0; first < 256; ++first)
    for (var second = 0; second < 256; ++second)
    for (var channel = 0; channel < 3; ++channel) {
      int a = gtia[first * 3 + channel], b = gtia[second * 3 + channel];
      blend[((first << 8 | second) * 3) + channel] = (byte)((a & b) + (((a ^ b) >> 1) & 0x7F));
    }

    var state = new _Encoder(rgb, blend, gtia);
    state.Run();

    return new() { Data = state.Assemble(), Height = PerRowHeight };
  }

  /// <summary>Holds the four things a Champions' Interlace picture is chosen from.</summary>
  private sealed class _Encoder {

    private readonly byte[] _rgb;
    private readonly byte[] _blend;
    private readonly byte[] _registers = new byte[PerRowHeight * Atari8BitGraphics.Gr15RegisterCount];
    private readonly int[] _luminance = new int[PerRowHeight * LogicalWidth];
    private readonly int[] _firstHue = new int[PerRowHeight / 2 * HueNibbles];
    private readonly int[] _secondHue = new int[PerRowHeight / 2 * HueNibbles];

    public _Encoder(byte[] rgb, byte[] blend, ReadOnlySpan<byte> gtia) {
      this._rgb = rgb;
      this._blend = blend;
      this._Initialise(gtia);
    }

    /// <summary>
    /// Starts every scanline off with the four luminances it asks for most and the hue each nibble
    /// is nearest to, which is the picture the format would draw if it did not smear a row into its
    /// neighbours.
    /// </summary>
    private void _Initialise(ReadOnlySpan<byte> gtia) {
      Span<int> counts = stackalloc int[16];
      var wanted = new int[LogicalWidth];

      for (var y = 0; y < PerRowHeight; ++y) {
        counts.Clear();

        for (var pixel = 0; pixel < LogicalWidth; ++pixel) {
          var at = (y * Width + pixel * 2) * 3;
          var colour = Atari8BitGraphics.FindNearestColorByte(gtia, this._rgb[at], this._rgb[at + 1], this._rgb[at + 2]);
          wanted[pixel] = colour & 14;
          ++counts[wanted[pixel] >> 1];

          var hue = colour >> 4;
          var row = y >> 1;
          if ((y & 1) == 0)
            this._secondHue[row * HueNibbles + (pixel >> 1)] = hue;
          else
            this._firstHue[row * HueNibbles + (pixel >> 1)] = hue;
        }

        for (var slot = 0; slot < 4; ++slot) {
          var best = 0;
          for (var value = 1; value < 8; ++value)
            if (counts[value] > counts[best])
              best = value;

          this._registers[y * Atari8BitGraphics.Gr15RegisterCount + slot] = (byte)(best << 1);
          counts[best] = -1;
        }

        for (var pixel = 0; pixel < LogicalWidth; ++pixel) {
          var best = 0;
          for (var slot = 1; slot < 4; ++slot)
            if (Math.Abs(this._Register(y, slot) - wanted[pixel]) < Math.Abs(this._Register(y, best) - wanted[pixel]))
              best = slot;

          this._luminance[y * LogicalWidth + pixel] = best;
        }
      }
    }

    /// <summary>Improves the luminances and the hues against each other.</summary>
    public void Run() {
      for (var pass = 0; pass < _PASSES; ++pass) {
        for (var y = 0; y < PerRowHeight; ++y)
        for (var pixel = 0; pixel < LogicalWidth; ++pixel)
          this._ImproveLuminance(y, pixel);

        for (var row = 0; row < PerRowHeight / 2; ++row)
        for (var nibble = 0; nibble < HueNibbles; ++nibble) {
          this._ImproveHue(this._firstHue, row, nibble, row * 2 + 1);
          this._ImproveHue(this._secondHue, row, nibble, row * 2);
        }
      }
    }

    private void _ImproveLuminance(int y, int pixel) {
      // A row's luminance is its own field's on that scanline and half of what the hue rows either
      // side of it show, so moving it reaches the row above and the row below as well.
      var at = y * LogicalWidth + pixel;
      var firstRow = y - 1;
      var best = this._luminance[at];
      var bestCost = long.MaxValue;

      for (var slot = 0; slot < 4; ++slot) {
        this._luminance[at] = slot;

        var cost = this._Cost(firstRow, 3, pixel * 2, 2);
        if (cost >= bestCost)
          continue;

        bestCost = cost;
        best = slot;
      }

      this._luminance[at] = best;
    }

    private void _ImproveHue(int[] hue, int row, int nibble, int firstRow) {
      var at = row * HueNibbles + nibble;
      var best = hue[at];
      var bestCost = long.MaxValue;

      for (var value = 0; value < 16; ++value) {
        hue[at] = value;

        var cost = this._Cost(firstRow, 2, nibble * 4, 4);
        if (cost >= bestCost)
          continue;

        bestCost = cost;
        best = value;
      }

      hue[at] = best;
    }

    /// <summary>What the picture costs over a block of scanlines and screen columns as it stands.</summary>
    private long _Cost(int firstRow, int rowCount, int firstColumn, int columns) {
      long cost = 0;

      for (var y = firstRow; y < firstRow + rowCount; ++y) {
        if (y < 0 || y >= PerRowHeight)
          continue;

        for (var x = firstColumn; x < firstColumn + columns; ++x) {
          var pixel = x >> 1;
          var entry = ((this._First(y, pixel) << 8) | this._Second(y, pixel)) * 3;
          var source = (y * Width + x) * 3;
          int dr = this._blend[entry] - this._rgb[source];
          int dg = this._blend[entry + 1] - this._rgb[source + 1];
          int db = this._blend[entry + 2] - this._rgb[source + 2];
          cost += dr * dr + dg * dg + db * db;
        }
      }

      return cost;
    }

    /// <summary>The luminance a register holds; its hue never reaches the screen and its low bit does not either.</summary>
    private int _Register(int y, int slot)
      => this._registers[y * Atari8BitGraphics.Gr15RegisterCount + slot] & 14;

    private int _Luminance(int y, int pixel)
      => this._Register(y, this._luminance[y * LogicalWidth + pixel]);

    /// <summary>The colour byte the first field shows: its bitmap rows are the even scanlines.</summary>
    private int _First(int y, int pixel) {
      var row = y >> 1;

      if ((y & 1) == 0) {
        // The very first row has no hue row above it, so it keeps the register's own hue — which is
        // left grey rather than spent on a colour only one scanline would show.
        var above = row > 0 ? this._firstHue[(row - 1) * HueNibbles + (pixel >> 1)] : 0;

        return (above << 4) | this._Luminance(y, pixel);
      }

      var below = y == PerRowHeight - 1 ? 0 : this._Luminance(y + 1, pixel);

      return (this._firstHue[row * HueNibbles + (pixel >> 1)] << 4)
             | ((this._Luminance(y - 1, pixel) + below) >> 1);
    }

    /// <summary>The colour byte the second field shows: its bitmap rows are the odd scanlines.</summary>
    private int _Second(int y, int pixel) {
      var row = y >> 1;
      var hue = this._secondHue[row * HueNibbles + (pixel >> 1)] << 4;

      if ((y & 1) != 0)
        return hue | this._Luminance(y, pixel);

      var above = y > 0 ? this._Luminance(y - 1, pixel) : 0;
      var below = y == PerRowHeight - 1 ? 0 : this._Luminance(y + 1, pixel);

      return hue | ((above + below) >> 1);
    }

    /// <summary>Lays the bitmap, the hues and the registers out where the reader expects them.</summary>
    public byte[] Assemble() {
      var data = new byte[PerRowSize];

      for (var y = 0; y < PerRowHeight; ++y) {
        for (var pixel = 0; pixel < LogicalWidth; ++pixel)
          data[y * Stride + (pixel >> 2)] |=
            (byte)(this._luminance[y * LogicalWidth + pixel] << ((3 - (pixel & 3)) << 1));

        for (var slot = 0; slot < Atari8BitGraphics.Gr15RegisterCount; ++slot)
          data[BareSize + y + slot * RegisterPlane] =
            this._registers[y * Atari8BitGraphics.Gr15RegisterCount + slot];
      }

      // The hue rows of the two fields alternate, the second field's first, and each is eighty bytes
      // from the next because the pair of them shares a row of the file.
      var hues = Stride * PerRowHeight;
      for (var row = 0; row < PerRowHeight / 2; ++row)
      for (var nibble = 0; nibble < HueNibbles; ++nibble) {
        var shift = (nibble & 1) == 0 ? 4 : 0;
        data[hues + row * HueStride + (nibble >> 1)] |= (byte)(this._secondHue[row * HueNibbles + nibble] << shift);
        data[hues + Stride + row * HueStride + (nibble >> 1)] |=
          (byte)(this._firstHue[row * HueNibbles + nibble] << shift);
      }

      return data;
    }
  }
}
