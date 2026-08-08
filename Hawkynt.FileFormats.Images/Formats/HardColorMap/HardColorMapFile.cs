using System;
using FileFormat.Core;

namespace FileFormat.HardColorMap;

/// <summary>In-memory representation of a Hard Color Map picture (.hcm).</summary>
/// <remarks>
/// A four-colour Atari screen with every sprite the machine has stretched to its widest and laid
/// across it — and moved, mid-scanline, so the same sprites cover the left half of the picture and
/// then the right. That is what the name refers to: the colour map is not a plane of data but the
/// timing of eight objects being repositioned twice per line, 192 times a frame.
/// <para/>
/// It cannot be read as a layout for that reason. What a pixel shows depends on which objects
/// happen to cover it and on the priority register's ranking of them, so the only way to know is to
/// run the chip.
/// </remarks>
public readonly record struct HardColorMapFile
  : IImageFormatReader<HardColorMapFile>, IImageToRawImage<HardColorMapFile>,
    IImageFromRawImage<HardColorMapFile>, IImageFormatWriter<HardColorMapFile> {

  /// <summary>Colours the playfield alone draws: the background and three registers.</summary>
  public const int PlayfieldColorCount = 4;

  /// <summary>Pixels across.</summary>
  public const int Width = 256;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Characters ANTIC fetches per scanline.</summary>
  public const int Columns = 32;

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "HCMA8";

  /// <summary>Offset of the first player's shapes.</summary>
  public const int FirstPlayerOffset = 48;

  /// <summary>Offset of the second player's shapes.</summary>
  public const int SecondPlayerOffset = 304;

  /// <summary>Offset of the sprite shapes the chip fetches per scanline.</summary>
  public const int SpriteDmaOffset = 816;

  /// <summary>Offset of the playfield.</summary>
  public const int PlayfieldOffset = 2064;

  /// <summary>Total file size.</summary>
  public const int FileSize = 8208;

  static string IImageFormatMetadata<HardColorMapFile>.PrimaryExtension => ".hcm";
  static string[] IImageFormatMetadata<HardColorMapFile>.FileExtensions => [".hcm"];
  static HardColorMapFile IImageFormatReader<HardColorMapFile>.FromSpan(ReadOnlySpan<byte> data)
    => HardColorMapReader.FromSpan(data);
  static byte[] IImageFormatWriter<HardColorMapFile>.ToBytes(HardColorMapFile file)
    => HardColorMapWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HardColorMapFile>.VideoModes => [
    new("Hard Color Map", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Which sprite sits on the left, which follows from the priority arrangement.</summary>
  public int LeftSprite { get; init; }

  /// <summary>The priority register the picture was drawn against.</summary>
  public int Priority { get; init; }

  public static RawImage ToRawImage(HardColorMapFile file) {
    var data = file.Data ?? [];
    var gtia = new _Renderer(data) { Priority = file.Priority, PlayfieldColumns = Columns };
    var left = file.LeftSprite;

    // Both halves of the screen are drawn by the same four players and four missiles, all at their
    // widest, so each colour appears twice across the line at two different positions.
    gtia.SetPlayerHpos(3, 104);
    gtia.SetPlayerHpos(3 - left, 104);
    gtia.SetMissileHpos(left, 136);
    gtia.SetMissileHpos(0, 136);
    gtia.SetMissileHpos(3, 144);
    gtia.SetMissileHpos(3 - left, 144);

    for (var i = 0; i < GtiaRenderer.SpriteCount; ++i) {
      gtia.SetPlayerSize(i, 3);
      gtia.SetMissileSizes(255);
    }

    gtia.Colors[GtiaRenderer.BackgroundRegister] = (byte)(data[7] & 254);
    gtia.Colors[3 - left] = gtia.Colors[0] = (byte)(data[8] & 254);
    gtia.Colors[3] = gtia.Colors[left] = (byte)(data[9] & 254);
    gtia.Colors[4] = (byte)(data[10] & 254);
    gtia.Colors[5] = (byte)(data[11] & 254);
    gtia.Colors[6] = (byte)(data[12] & 254);

    var frame = new byte[Width * Height];

    for (var y = 0; y < Height; ++y) {
      gtia.SetPlayerHpos(left, 72);
      gtia.SetPlayerHpos(0, 72);
      gtia.ProcessSpriteDma(data, SpriteDmaOffset + y);
      gtia.StartLine(64);
      gtia.DrawSpan(y, 64, 128, AnticMode.FourColor, frame, Width, 0);

      // Half way across, the same two players are moved right and given new shapes.
      gtia.SetPlayerHpos(left, 152);
      gtia.SetPlayerHpos(0, 152);
      gtia.SetPlayerGraphics(0, data[FirstPlayerOffset + y]);
      gtia.SetPlayerGraphics(left, data[SecondPlayerOffset + y]);
      gtia.DrawSpan(y, 128, 192, AnticMode.FourColor, frame, Width, 0);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Writes a picture as the playfield alone, with every sprite left empty.</summary>
  /// <remarks>
  /// The colour map the name refers to is eight objects at their widest being repositioned twice per
  /// scanline, 192 times a frame. What a pixel then shows depends on which of them happen to cover
  /// it and on the priority register's ranking, so choosing shapes and positions to make a wanted
  /// picture is not a layout problem at all — it is a search over the chip's behaviour, and one this
  /// decoder can only score rather than guide.
  /// <para/>
  /// So the sprites are left empty and the four colours the playfield draws by itself are used: the
  /// background and three registers, at two screen pixels per stored pixel. That is a quarter of
  /// what the format can show and all of what an encoder can be sure of.
  /// </remarks>
  public static HardColorMapFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.SampleTo(Width, Height);
    var registers = Atari8BitGraphics.ChooseGr15Registers(
      PixelConverter.Convert(source, PixelFormat.Bgra32).PixelData, Width * Height, PlayfieldColorCount);

    var data = new byte[FileSize];
    for (var i = 0; i < Signature.Length; ++i)
      data[i] = (byte)Signature[i];

    data[5] = 1;
    data[7] = registers[0];
    data[10] = registers[1];
    data[11] = registers[2];
    data[12] = registers[3];

    var gtia = Atari8BitGraphics.Palette;
    for (var y = 0; y < Height; ++y)
    for (var column = 0; column < Columns; ++column) {
      byte bits = 0;
      for (var pixel = 0; pixel < 4; ++pixel)
        bits |= (byte)(_ChoosePattern(source.PixelData, registers, gtia, y, (column * 4 + pixel) * 2)
                       << (6 - (pixel << 1)));

      data[PlayfieldOffset + (y << 5) + column] = bits;
    }

    return new() { Data = data, LeftSprite = 2, Priority = 0 };
  }

  /// <summary>The pattern whose register is nearest the two screen pixels it covers.</summary>
  private static int _ChoosePattern(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> registers, ReadOnlySpan<byte> gtia, int y, int x) {
    var best = 0;
    var bestCost = long.MaxValue;

    for (var pattern = 0; pattern < PlayfieldColorCount; ++pattern) {
      var entry = registers[pattern] * 3;
      long cost = 0;

      for (var offset = 0; offset < 2; ++offset) {
        var at = (y * Width + x + offset) * 3;
        long dr = rgb[at] - gtia[entry], dg = rgb[at + 1] - gtia[entry + 1], db = rgb[at + 2] - gtia[entry + 2];
        cost += dr * dr + dg * dg + db * db;
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = pattern;
    }

    return best;
  }

  /// <summary>The playfield is a plain 32-byte row per scanline.</summary>
  private sealed class _Renderer(byte[] data) : GtiaRenderer {

    protected override int GetPlayfieldByte(int y, int column) {
      var at = PlayfieldOffset + (y << 5) + column;

      return at >= 0 && at < data.Length ? data[at] : 0;
    }
  }
}
