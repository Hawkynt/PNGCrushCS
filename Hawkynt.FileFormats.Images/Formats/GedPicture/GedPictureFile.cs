using System;
using FileFormat.Core;

namespace FileFormat.GedPicture;

/// <summary>In-memory representation of a GED picture (.ged).</summary>
/// <remarks>
/// A four-colour Atari screen whose three playfield colours are rewritten six times across every
/// scanline. The register writes are not free — each costs cycles the processor does not have
/// spare — so the positions they land at are fixed by the timing rather than chosen, and the file
/// stores which of eight timings the picture was drawn against rather than the positions
/// themselves.
/// <para/>
/// One further register per scanline may be anything at all: the file stores an address and a value
/// and pokes them, which is what lets a picture change a sprite's position or the priority ranking
/// part way down the screen.
/// </remarks>
public readonly record struct GedPictureFile
  : IImageFormatReader<GedPictureFile>, IImageToRawImage<GedPictureFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Characters ANTIC fetches per scanline.</summary>
  public const int Columns = 40;

  /// <summary>The six bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [255, 255, 48, 83, 79, 127];

  /// <summary>Total file size.</summary>
  public const int FileSize = 11302;

  /// <summary>Offset of the free register write's value, one per scanline.</summary>
  public const int PokeValueOffset = 6;

  /// <summary>Offset of the free register write's address, one per scanline.</summary>
  public const int PokeAddressOffset = 206;

  /// <summary>Offset of the first of the six colour tables, one entry per scanline.</summary>
  public const int ColorTablesOffset = 406;

  /// <summary>Offset of the sprite shapes the chip fetches per scanline.</summary>
  public const int SpriteDmaOffset = 2034;

  /// <summary>Offset of the player sizes.</summary>
  public const int PlayerSizeOffset = 3290;

  /// <summary>Offset of the playfield.</summary>
  public const int PlayfieldOffset = 3302;

  static string IImageFormatMetadata<GedPictureFile>.PrimaryExtension => ".ged";
  static string[] IImageFormatMetadata<GedPictureFile>.FileExtensions => [".ged"];
  static GedPictureFile IImageFormatReader<GedPictureFile>.FromSpan(ReadOnlySpan<byte> data)
    => GedPictureReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<GedPictureFile>.VideoModes => [
    new("GED", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Which of the eight timings the register writes were made against.</summary>
  public int Cycle { get; init; }

  public static RawImage ToRawImage(GedPictureFile file) {
    var data = file.Data ?? [];
    var cycle = file.Cycle;

    var gtia = new _Renderer(data) { PlayfieldColumns = Columns, Priority = data[3292] };
    gtia.SetMissileSizes(data[3291]);
    gtia.Colors[7] = (byte)(data[3293] & 254);
    gtia.Colors[GtiaRenderer.BackgroundRegister] = (byte)(data[3294] & 254);

    for (var i = 0; i < GtiaRenderer.SpriteCount; ++i) {
      // The four player sizes share one byte, most significant pair first.
      gtia.SetPlayerSize(i, data[PlayerSizeOffset] >> ((3 - i) << 1));
      gtia.SetPlayerHpos(i, 48 + data[3295 + i]);

      // Where the missiles sit depends on whether they are objects or a fifth playfield colour:
      // as objects each follows its player, and as a playfield they line up beside each other.
      gtia.SetMissileHpos(i, (gtia.Priority & 16) == 0
        ? 48 + data[3295 + i] + (gtia.PlayerSize(i) << 3)
        : i == 0 ? 48 + data[3299] : _MissileHpos(gtia, data, i));

      gtia.Colors[i] = (byte)(data[3286 + i] & 254);
    }

    var frame = new byte[Width * Height];

    for (var y = 0; y < Height; ++y) {
      gtia.ProcessSpriteDma(data, SpriteDmaOffset + y);

      // One register of the scanline's choosing, addressed within the chip.
      gtia.Poke(data[PokeAddressOffset + y] & 31, data[PokeValueOffset + y]);

      gtia.Colors[4] = (byte)(data[ColorTablesOffset + y] & 254);
      gtia.Colors[5] = (byte)(data[ColorTablesOffset + 200 + y] & 254);
      gtia.Colors[6] = (byte)(data[ColorTablesOffset + 400 + y] & 254);

      gtia.StartLine(48);
      var hpos = gtia.DrawSpan(y, 48, 63 + (cycle << 3), AnticMode.FourColor, frame, Width, 0);

      gtia.Colors[4] = (byte)(data[ColorTablesOffset + 600 + y] & 254);
      hpos = gtia.DrawSpan(
        y, hpos, cycle < 4 ? hpos + 32 : 107 + (cycle << 2), AnticMode.FourColor, frame, Width, 0);

      gtia.Colors[5] = (byte)(data[ColorTablesOffset + 800 + y] & 254);
      hpos = gtia.DrawSpan(y, hpos, 123 + (cycle << 2), AnticMode.FourColor, frame, Width, 0);

      gtia.Colors[6] = (byte)(data[ColorTablesOffset + 1000 + y] & 254);
      hpos = gtia.DrawSpan(y, hpos, hpos + 24, AnticMode.FourColor, frame, Width, 0);

      gtia.Colors[4] = (byte)(data[ColorTablesOffset + 1200 + y] & 254);
      hpos = gtia.DrawSpan(y, hpos, hpos + 24, AnticMode.FourColor, frame, Width, 0);

      gtia.Colors[5] = (byte)(data[ColorTablesOffset + 1400 + y] & 254);
      gtia.DrawSpan(y, hpos, 208, AnticMode.FourColor, frame, Width, 0);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Where a missile sits when the missiles form a playfield: just past the previous one.</summary>
  private static int _MissileHpos(GtiaRenderer gtia, ReadOnlySpan<byte> data, int missile) {
    var hpos = 48 + data[3299];
    for (var i = 1; i <= missile; ++i)
      hpos += gtia.MissileSize(i - 1) << 1;

    return hpos;
  }

  /// <summary>The playfield is a plain forty-byte row per scanline.</summary>
  private sealed class _Renderer(byte[] data) : GtiaRenderer {

    protected override int GetPlayfieldByte(int y, int column) {
      var at = PlayfieldOffset + y * Columns + column;

      return at >= 0 && at < data.Length ? data[at] : 0;
    }
  }
}
