using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PowerGraphics;

/// <summary>In-memory representation of a PowerGraphics picture (.pgr).</summary>
/// <remarks>
/// Not a picture but the display program that draws one. The file carries ANTIC's own display list
/// and, for every scanline, a stream of instructions saying which chip register to write and when —
/// counted in processor cycles, because that is what decides where along the line the write lands.
/// <para/>
/// So the mode changes down the screen, the colours change across it, and neither is stored
/// anywhere as data. The only way to know what the picture shows is to run the program against the
/// hardware's timing, which is what makes this the format the renderer exists for.
/// </remarks>
public readonly record struct PowerGraphicsFile
  : IImageFormatReader<PowerGraphicsFile>, IImageToRawImage<PowerGraphicsFile>,
    IImageFromRawImage<PowerGraphicsFile>, IImageFormatWriter<PowerGraphicsFile> {

  /// <summary>Pixels across, including the borders the raster instructions can reach.</summary>
  public const int Width = 336;

  /// <summary>Rows.</summary>
  public const int Height = 240;

  /// <summary>The text every file carries where a name would go.</summary>
  public const string Signature = "PowerGFX";

  /// <summary>Offset of the display list.</summary>
  public const int DisplayListOffset = 16;

  /// <summary>Where the machine loaded the file, which the stored addresses are relative to.</summary>
  public const int LoadAddress = 33280;

  static string IImageFormatMetadata<PowerGraphicsFile>.PrimaryExtension => ".pgr";
  static string[] IImageFormatMetadata<PowerGraphicsFile>.FileExtensions => [".pgr"];
  static PowerGraphicsFile IImageFormatReader<PowerGraphicsFile>.FromSpan(ReadOnlySpan<byte> data)
    => PowerGraphicsReader.FromSpan(data);
  static byte[] IImageFormatWriter<PowerGraphicsFile>.ToBytes(PowerGraphicsFile file)
    => PowerGraphicsWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PowerGraphicsFile>.VideoModes => [
    new("PowerGraphics", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Characters ANTIC fetches per scanline.</summary>
  public int Columns { get; init; }

  /// <summary>What ANTIC's own control register says about sprite fetching.</summary>
  public int DmaControl { get; init; }

  public static RawImage ToRawImage(PowerGraphicsFile file) {
    var data = file.Data ?? [];
    var gtia = new _Renderer(data) { PlayfieldColumns = file.Columns };

    // The chip's registers are set from two blocks, one for the players and one for everything else.
    for (var i = 0; i < 14; ++i) {
      gtia.Poke(i, data[504 + i]);
      gtia.Poke(14 + i, data[760 + i]);
    }

    var frame = new byte[Width * Height];
    var displayList = DisplayListOffset;
    var raster = (data[6] | (data[7] << 8)) - LoadAddress;
    var accumulator = 0;

    for (var y = 0; y < Height; ++y) {
      var hpos = -11;
      var instruction = data[displayList++];

      switch (instruction) {
        case 0 or 14 or 15:
          break;

        // A jump instruction is not consumed: the same one governs every line of its block.
        case 65:
          --displayList;
          break;

        case 78 or 79:
          hpos += 4;
          gtia.ScreenOffset = (data[displayList] | (data[displayList + 1] << 8)) - LoadAddress;
          displayList += 2;
          break;

        default:
          throw new InvalidDataException($"A PowerGraphics display list holds no instruction {instruction}.");
      }

      var anticMode = (instruction & 15) switch {
        14 => AnticMode.FourColor,
        15 => AnticMode.HiRes,
        _ => AnticMode.Blank,
      };

      gtia.StartLine(44);

      // Fetching sprites costs the processor cycles and shifts everything after it.
      if ((file.DmaControl & 12) != 0) {
        hpos += 2;
        if ((file.DmaControl & 4) != 0)
          gtia.MissileGraphics = data[264 + y];

        if ((file.DmaControl & 8) != 0) {
          hpos += 8;
          gtia.ProcessPlayerDma(data, 520 + y);
        }
      }

      for (var cycles = 1;;) {
        var operation = data[raster++];

        // A set bit says the value to write follows; otherwise the last one is reused.
        if ((operation & 32) != 0) {
          cycles += 2;
          accumulator = data[raster++];
        }

        var address = operation & 31;
        if (address <= 27) {
          var until = gtia.AdvanceCpuCycles(hpos, cycles, anticMode != AnticMode.Blank);
          gtia.DrawSpan(y, Math.Max(hpos, 44), Math.Min(until, 212), anticMode, frame, Width, 0);
          hpos = until;
          gtia.Poke(address, accumulator);

          if (operation >= 128)
            break;

          cycles = 4;
          continue;
        }

        // Anything above the register range is a count of do-nothing instructions, spent to place
        // the next write where the artist wanted it.
        var nops = ((operation >> 6) & 3) | ((operation & 3) << 2);
        if (nops == 0)
          break;

        cycles += nops << 1;
      }

      gtia.DrawSpan(y, Math.Max(hpos, 44), 212, anticMode, frame, Width, 0);

      if (anticMode != AnticMode.Blank)
        gtia.ScreenOffset += file.Columns;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Writes the display program that draws a picture.</summary>
  /// <remarks>
  /// The program written is the plainest one the format allows: mode E all the way down, and four
  /// register writes at the blanked start of every scanline, which is as many as the cycles before
  /// the picture begins will hold. See <see cref="PowerGraphicsEncoder"/>.
  /// </remarks>
  public static PowerGraphicsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return PowerGraphicsReader.FromBytes(PowerGraphicsEncoder.Encode(image.SampleTo(Width, Height).PixelData));
  }

  /// <summary>The playfield comes from wherever the display list last pointed ANTIC.</summary>
  private sealed class _Renderer(byte[] data) : GtiaRenderer {

    /// <summary>Where ANTIC is fetching the current scanline from.</summary>
    public int ScreenOffset { get; set; } = -1;

    protected override int GetPlayfieldByte(int y, int column) {
      var at = this.ScreenOffset + column;

      return at >= 0 && at < data.Length ? data[at] : 0;
    }
  }
}
