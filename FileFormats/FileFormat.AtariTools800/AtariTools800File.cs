using System;
using FileFormat.Core;

namespace FileFormat.AtariTools800;

/// <summary>In-memory representation of an AtariTools-800 sprite dump (.4pl, .4mi, .4pm).</summary>
/// <remarks>
/// Not a picture but the hardware's sprite registers laid out flat. A player is eight bits wide and
/// 240 scanlines tall with one colour of its own; a missile is two bits wide with the same. The
/// files show all four of one or both kinds side by side, each sprite at the fixed position the
/// dump gives it, on black — so the gaps between them are part of the image, not padding.
/// </remarks>
public readonly record struct AtariTools800File
  : IImageFormatReader<AtariTools800File>, IImageToRawImage<AtariTools800File>,
    IImageFromRawImage<AtariTools800File>, IImageFormatWriter<AtariTools800File> {

  /// <summary>Scanlines every sprite spans.</summary>
  public const int Height = 240;

  /// <summary>Sprites of each kind a file holds.</summary>
  public const int SpriteCount = 4;

  /// <summary>Screen pixels a player occupies: eight bits, each drawn twice.</summary>
  public const int PlayerWidth = 16;

  /// <summary>Distance between two players' left edges.</summary>
  public const int PlayerPitch = 20;

  /// <summary>Screen pixels a missile occupies: two bits, each drawn twice.</summary>
  public const int MissileWidth = 4;

  /// <summary>Distance between two missiles' left edges.</summary>
  public const int MissilePitch = 8;

  /// <summary>Width of the players section.</summary>
  public const int PlayersWidth = SpriteCount * PlayerPitch;

  /// <summary>Width of the missiles section.</summary>
  public const int MissilesWidth = SpriteCount * MissilePitch;

  /// <summary>Bytes of shape data one player uses.</summary>
  public const int PlayerDataSize = Height;

  /// <summary>Bytes of shape data all four missiles share, two bits each per scanline.</summary>
  public const int MissileDataSize = Height;

  static string IImageFormatMetadata<AtariTools800File>.PrimaryExtension => ".4pl";
  static string[] IImageFormatMetadata<AtariTools800File>.FileExtensions => [".4pl", ".4mi", ".4pm"];
  static AtariTools800File IImageFormatReader<AtariTools800File>.FromSpan(ReadOnlySpan<byte> data) => AtariTools800Reader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariTools800File>.ToBytes(AtariTools800File file) => AtariTools800Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariTools800File>.VideoModes => [
    new("Players", [(PlayersWidth, Height)], [SpriteCount + 1]),
    new("Missiles", [(MissilesWidth, Height)], [SpriteCount + 1]),
    new("Players and missiles", [(PlayersWidth + MissilesWidth, Height)], [SpriteCount * 2 + 1]),
  ];

  /// <summary>Whether a kind carries players.</summary>
  public static bool HasPlayers(AtariTools800Kind kind) => kind != AtariTools800Kind.Missiles;

  /// <summary>Whether a kind carries missiles.</summary>
  public static bool HasMissiles(AtariTools800Kind kind) => kind != AtariTools800Kind.Players;

  /// <summary>Displayed width.</summary>
  public static int WidthFor(AtariTools800Kind kind)
    => (HasPlayers(kind) ? PlayersWidth : 0) + (HasMissiles(kind) ? MissilesWidth : 0);

  /// <summary>Offset of the missile shape data.</summary>
  public static int MissileDataOffsetFor(AtariTools800Kind kind)
    => SpriteCount + (HasPlayers(kind) ? SpriteCount * PlayerDataSize : 0);

  /// <summary>Total file size: the four colour bytes, then the shape data.</summary>
  public static int FileSizeFor(AtariTools800Kind kind)
    => MissileDataOffsetFor(kind) + (HasMissiles(kind) ? MissileDataSize : 0);

  /// <summary>Maps an extension to the dump it names.</summary>
  public static AtariTools800Kind KindFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".4mi" => AtariTools800Kind.Missiles,
    ".4pm" => AtariTools800Kind.PlayersAndMissiles,
    _ => AtariTools800Kind.Players,
  };

  /// <summary>Which dump this is.</summary>
  public AtariTools800Kind Kind { get; init; }

  /// <summary>One Atari colour byte per sprite; players and missiles of the same number share it.</summary>
  public byte[] Colors { get; init; }

  /// <summary>Shape data for the four players, 240 bytes each; empty when the file has none.</summary>
  public byte[] PlayerData { get; init; }

  /// <summary>Shape data for the four missiles, two bits each per scanline; empty when there are none.</summary>
  public byte[] MissileData { get; init; }

  public static RawImage ToRawImage(AtariTools800File file) {
    var kind = file.Kind;
    var width = WidthFor(kind);
    var colors = file.Colors ?? [];

    // Index 0 is the black background every sprite sits on; each sprite adds one colour.
    var gtia = Atari8BitGraphics.CreatePalette();
    var palette = new byte[(SpriteCount + 1) * 3];
    for (var sprite = 0; sprite < SpriteCount; ++sprite) {
      var color = sprite < colors.Length ? colors[sprite] & 254 : 0;
      Array.Copy(gtia, color * 3, palette, (sprite + 1) * 3, 3);
    }

    var pixels = new byte[width * Height];
    if (HasPlayers(kind))
      _DrawPlayers(file.PlayerData ?? [], pixels, width, 0);

    if (HasMissiles(kind))
      _DrawMissiles(file.MissileData ?? [], pixels, width, HasPlayers(kind) ? PlayersWidth : 0);

    return new() {
      Width = width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = SpriteCount + 1,
    };
  }

  private static void _DrawPlayers(byte[] shapes, byte[] pixels, int width, int left) {
    for (var sprite = 0; sprite < SpriteCount; ++sprite)
    for (var y = 0; y < Height; ++y) {
      var index = sprite * PlayerDataSize + y;
      var bits = index < shapes.Length ? shapes[index] : 0;
      for (var bit = 0; bit < 8; ++bit) {
        if (((bits >> (7 - bit)) & 1) == 0)
          continue;

        var offset = y * width + left + sprite * PlayerPitch + bit * 2;
        pixels[offset] = pixels[offset + 1] = (byte)(sprite + 1);
      }
    }
  }

  private static void _DrawMissiles(byte[] shapes, byte[] pixels, int width, int left) {
    for (var y = 0; y < Height; ++y) {
      var bits = y < shapes.Length ? shapes[y] : 0;
      for (var sprite = 0; sprite < SpriteCount; ++sprite) {
        // Two bits per missile, the higher one drawn to the left.
        var pair = bits >> (sprite << 1);
        var offset = y * width + left + sprite * MissilePitch;
        if ((pair & 2) != 0)
          pixels[offset] = pixels[offset + 1] = (byte)(sprite + 1);

        if ((pair & 1) != 0)
          pixels[offset + 2] = pixels[offset + 3] = (byte)(sprite + 1);
      }
    }
  }

  public static AtariTools800File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var kind = image.Width switch {
      PlayersWidth => AtariTools800Kind.Players,
      MissilesWidth => AtariTools800Kind.Missiles,
      PlayersWidth + MissilesWidth => AtariTools800Kind.PlayersAndMissiles,
      _ => throw new ArgumentException(
        $"An AtariTools-800 dump is {PlayersWidth}, {MissilesWidth} or {PlayersWidth + MissilesWidth} pixels wide, got {image.Width}.", nameof(image)),
    };

    if (image.Height != Height)
      throw new ArgumentException($"An AtariTools-800 dump is {Height} scanlines tall, got {image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var gtia = Atari8BitGraphics.CreatePalette();

    var colors = new byte[SpriteCount];
    var players = HasPlayers(kind) ? new byte[SpriteCount * PlayerDataSize] : [];
    var missiles = HasMissiles(kind) ? new byte[MissileDataSize] : [];

    // Each sprite owns a fixed column band, so it is encoded from that band alone: its colour is
    // the average of what is lit there, and a pixel is lit when it is nearer that than black.
    if (HasPlayers(kind))
      for (var sprite = 0; sprite < SpriteCount; ++sprite)
        colors[sprite] = _EncodePlayer(bgra.PixelData, image.Width, gtia, sprite, players);

    if (HasMissiles(kind)) {
      var left = HasPlayers(kind) ? PlayersWidth : 0;
      for (var sprite = 0; sprite < SpriteCount; ++sprite) {
        var color = _EncodeMissile(bgra.PixelData, image.Width, gtia, sprite, left, missiles);
        // A player and its missile share one register, so players win where both have an opinion.
        if (!HasPlayers(kind) || colors[sprite] == 0)
          colors[sprite] = color;
      }
    }

    return new() { Kind = kind, Colors = colors, PlayerData = players, MissileData = missiles };
  }

  private static byte _EncodePlayer(byte[] bgra, int width, byte[] gtia, int sprite, byte[] shapes) {
    var left = sprite * PlayerPitch;
    var color = _AverageLitColor(bgra, width, gtia, left, PlayerWidth);

    for (var y = 0; y < Height; ++y) {
      var bits = 0;
      for (var bit = 0; bit < 8; ++bit)
        if (_IsLit(bgra, (y * width + left + bit * 2) * 4, gtia, color))
          bits |= 0x80 >> bit;

      shapes[sprite * PlayerDataSize + y] = (byte)bits;
    }

    return color;
  }

  private static byte _EncodeMissile(byte[] bgra, int width, byte[] gtia, int sprite, int left, byte[] shapes) {
    var origin = left + sprite * MissilePitch;
    var color = _AverageLitColor(bgra, width, gtia, origin, MissileWidth);

    for (var y = 0; y < Height; ++y) {
      var bits = 0;
      if (_IsLit(bgra, (y * width + origin) * 4, gtia, color))
        bits |= 2;

      if (_IsLit(bgra, (y * width + origin + 2) * 4, gtia, color))
        bits |= 1;

      shapes[y] |= (byte)(bits << (sprite << 1));
    }

    return color;
  }

  /// <summary>The Atari colour closest to the average of everything non-black in a sprite's band.</summary>
  private static byte _AverageLitColor(byte[] bgra, int width, byte[] gtia, int left, int bandWidth) {
    long red = 0, green = 0, blue = 0, lit = 0;
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < bandWidth; ++x) {
      var pixel = (y * width + left + x) * 4;
      if (bgra[pixel] + bgra[pixel + 1] + bgra[pixel + 2] < 96)
        continue;

      red += bgra[pixel + 2];
      green += bgra[pixel + 1];
      blue += bgra[pixel];
      ++lit;
    }

    return lit == 0
      ? (byte)0
      : Atari8BitGraphics.FindNearestColorByte(gtia, (byte)(red / lit), (byte)(green / lit), (byte)(blue / lit));
  }

  private static bool _IsLit(byte[] bgra, int pixel, byte[] gtia, byte color) {
    int red = bgra[pixel + 2], green = bgra[pixel + 1], blue = bgra[pixel];
    var toBlack = red * red + green * green + blue * blue;
    int dr = gtia[color * 3] - red, dg = gtia[color * 3 + 1] - green, db = gtia[color * 3 + 2] - blue;

    return dr * dr + dg * dg + db * db < toBlack;
  }
}
