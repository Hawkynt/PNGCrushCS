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
  : IImageFormatReader<HardColorMapFile>, IImageToRawImage<HardColorMapFile> {

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

  /// <summary>The playfield is a plain 32-byte row per scanline.</summary>
  private sealed class _Renderer(byte[] data) : GtiaRenderer {

    protected override int GetPlayfieldByte(int y, int column) {
      var at = PlayfieldOffset + (y << 5) + column;

      return at >= 0 && at < data.Length ? data[at] : 0;
    }
  }
}
