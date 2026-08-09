using System;
using FileFormat.Core;

namespace FileFormat.ColoRix;

/// <summary>In-memory representation of a ColoRIX VGA paint image.</summary>
[FormatMagicBytes([0x52, 0x49, 0x58, 0x33])]
public readonly record struct ColoRixFile : IImageFormatReader<ColoRixFile>, IImageToRawImage<ColoRixFile>, IImageFromRawImage<ColoRixFile>, IImageFormatWriter<ColoRixFile> {

  /// <summary>The VGA palette type marker (0xAF).</summary>
  internal const byte VgaPaletteType = 0xAF;

  /// <summary>Size of a VGA palette in bytes (256 entries x 3 bytes).</summary>
  internal const int PaletteSize = 768;

  /// <summary>Size of the file header in bytes.</summary>
  internal const int HeaderSize = 10;

  static string IImageFormatMetadata<ColoRixFile>.PrimaryExtension => ".rix";

  /// <summary>The suffixed names are the same format under the screen mode it was saved in.</summary>
  /// <remarks>
  /// XnView's catalogue writes this row's extensions as <c>rix sci scx sc?</c>, one decoder for all
  /// of them: the trailing character is the ColoRIX screen mode and the header behind it is
  /// identical. <c>sc?</c> is a wildcard rather than a name — the only one in a catalogue of 554
  /// entries — and it stands for <c>sc</c> and any one character, so every one of those is listed
  /// here.
  /// <para/>
  /// Claiming that many names costs nothing because the extension decides nothing. XnView's own
  /// converter identifies this format from the bytes and reads a ColoRIX picture under any name at
  /// all, including names belonging to other formats entirely; the wildcard is what its file chooser
  /// offers, not what its reader tests. Half of this set is spoken for here by something else —
  /// <c>.scr</c> by four formats, <c>.sca</c> and <c>.scb</c> by MSX Screen 10, <c>.scf</c> by
  /// SciFax, <c>.sct</c> by Scitex — and none of that matters either, because a file under any of
  /// these names still has to open with <c>RIX3</c> before this reader takes it.
  /// </remarks>
  static string[] IImageFormatMetadata<ColoRixFile>.FileExtensions => [
    ".rix",
    ".sc0", ".sc1", ".sc2", ".sc3", ".sc4", ".sc5", ".sc6", ".sc7", ".sc8", ".sc9",
    ".sca", ".scb", ".scc", ".scd", ".sce", ".scf", ".scg", ".sch", ".sci", ".scj", ".sck", ".scl",
    ".scm", ".scn", ".sco", ".scp", ".scq", ".scr", ".scs", ".sct", ".scu", ".scv", ".scw", ".scx",
    ".scy", ".scz",
  ];
  static ColoRixFile IImageFormatReader<ColoRixFile>.FromSpan(ReadOnlySpan<byte> data) => ColoRixReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ColoRixFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];
  static byte[] IImageFormatWriter<ColoRixFile>.ToBytes(ColoRixFile file) => ColoRixWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>VGA palette (768 bytes: 256 entries x 3 bytes, 6-bit values 0-63).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Pixel data (width * height bytes of 8-bit palette indices).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Storage type (uncompressed or RLE).</summary>
  public ColoRixCompression StorageType { get; init; }

  /// <summary>Converts a ColoRIX file to a <see cref="RawImage"/> with Indexed8 format and 8-bit expanded palette.</summary>
  public static RawImage ToRawImage(ColoRixFile file) {

    var palette8Bit = new byte[PaletteSize];
    for (var i = 0; i < PaletteSize; ++i)
      palette8Bit[i] = (byte)((file.Palette[i] & 0x3F) * 255 / 63);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette8Bit,
      PaletteCount = 256,
    };
  }

  /// <summary>Creates a ColoRIX file from a <see cref="RawImage"/>. Must be Indexed8 with a 256-entry palette.</summary>
  public static ColoRixFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);
    if (image.Palette == null || image.Palette.Length < PaletteSize)
      throw new ArgumentException("ColoRIX requires a 256-entry RGB palette.", nameof(image));

    var palette6Bit = new byte[PaletteSize];
    for (var i = 0; i < PaletteSize; ++i)
      palette6Bit[i] = (byte)(image.Palette[i] * 63 / 255);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Palette = palette6Bit,
      PixelData = image.PixelData[..],
      StorageType = ColoRixCompression.None,
    };
  }
}
