using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PixelPowerCollage;

/// <summary>In-memory representation of a Pixel Power Collage picture.</summary>
/// <remarks>
/// This one authenticates against its own name. The first thirty-two bytes of the file hold the name
/// the file is meant to be saved under, terminated by a zero, and a reader compares them against the
/// name the file actually has — case does not matter, the extension does. A file renamed is a file
/// refused, which for a broadcast graphics system whose stills are addressed by name from a playout
/// list is not eccentric but the point: the name is part of the record, and a still that has been
/// renamed is no longer the still that was scheduled.
/// <para/>
/// So this is one of the few formats here that cannot be read from bytes alone, and the
/// <see cref="IImageFormatReader{TSelf}.FromSpan"/> entry says so rather than quietly skipping the
/// check. Everything after those thirty-two bytes is perfectly readable without a name; what is not
/// readable is whether the file is the one it claims to be, and answering that question wrongly is
/// worse than declining it.
/// <para/>
/// The four extensions select nothing. The layout comes from a code at 0x40 — thirty-two, twenty-four
/// or eight bits a pixel — and a file under any of the four names takes the same path.
/// </remarks>
public readonly record struct PixelPowerCollageFile : IImageFormatReader<PixelPowerCollageFile>, IImageToRawImage<PixelPowerCollageFile>, IImageFromRawImage<PixelPowerCollageFile>, IImageFormatWriter<PixelPowerCollageFile> {

  static string IImageFormatMetadata<PixelPowerCollageFile>.PrimaryExtension => ".i17";
  static string[] IImageFormatMetadata<PixelPowerCollageFile>.FileExtensions => [".i17", ".i18", ".ib7", ".if9"];

  /// <summary>Refuses, a picture that authenticates against its name not being readable without one.</summary>
  static PixelPowerCollageFile IImageFormatReader<PixelPowerCollageFile>.FromSpan(ReadOnlySpan<byte> data)
    => PixelPowerCollageReader.FromSpan(data);

  /// <summary>Reads a named file, which is the only way the name in it can be checked.</summary>
  static PixelPowerCollageFile IImageFormatReader<PixelPowerCollageFile>.FromFile(FileInfo file)
    => PixelPowerCollageReader.FromFile(file);

  static VideoMode[] IImageFormatMetadata<PixelPowerCollageFile>.VideoModes => [
    new("Collage", [(IntegerRange.Any, IntegerRange.Any)]),
  ];

  static byte[] IImageFormatWriter<PixelPowerCollageFile>.ToBytes(PixelPowerCollageFile file) => PixelPowerCollageWriter.ToBytes(file);

  /// <summary>Bytes at the head of the file holding the name it must be saved under.</summary>
  public const int NameSize = 32;

  /// <summary>What a picture encoded without a path is called, there being nowhere else to learn it.</summary>
  /// <remarks>
  /// Encoding to a byte array has no file name in it, and this format will not open without one that
  /// matches. So the array is built to be filed under this name and no other — save it as anything
  /// else and every reader, ours and XnView's, turns it away. A caller with a path should write
  /// through <see cref="FormatIO.WriteToFile{T}"/>, which puts the real name in.
  /// </remarks>
  public const string DefaultStem = "image";

  /// <summary>The extension a picture encoded without a path takes, the four being interchangeable.</summary>
  public const string DefaultExtension = ".i17";

  /// <summary>Where the picture itself begins.</summary>
  public const int PixelOffset = 0x80;

  /// <summary>Largest picture either way round.</summary>
  public const int MaximumExtent = 599999;

  /// <summary>The name the file must be filed under, extension included.</summary>
  public string Name { get; init; }

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>Thirty-two, twenty-four or eight.</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>The picture as it lies, from the top-left corner, with no padding at the row end.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Bytes a row takes: no padding, so exactly as many as the pixels need.</summary>
  public int Stride => this.Width * this.BitsPerPixel / 8;

  public static RawImage ToRawImage(PixelPowerCollageFile file) => file.BitsPerPixel switch {
    8 => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    },
    // Blue first, which is the order a Windows bitmap keeps and this one keeps too.
    24 => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Bgr24,
      PixelData = file.PixelData[..],
    },
    _ => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = _ReverseComponents(file.PixelData),
    },
  };

  /// <summary>Encodes a picture for a file that will be called <see cref="DefaultStem"/>.</summary>
  public static PixelPowerCollageFile FromRawImage(RawImage image)
    => FromRawImage(image, DefaultExtension);

  /// <summary>Encodes a picture for a file about to be given a name of its own.</summary>
  /// <remarks>
  /// The extension selects nothing here — all four names take the same layout — but it is still part
  /// of the name that gets written and compared, so a file destined for <c>.ib7</c> must carry
  /// <c>.ib7</c> in its head or it will not open.
  /// </remarks>
  public static PixelPowerCollageFile FromRawImage(RawImage image, string extension)
    => _Encode(image, DefaultStem + extension);

  /// <summary>Encodes a picture for the file about to be written at this path.</summary>
  /// <remarks>
  /// This is the entry that makes the format writable at all. The name in the header has to be the
  /// name on disk, so the encoding cannot happen until the path is known — which is why the picture
  /// is built here rather than by the overloads above, and why writing through a byte array can only
  /// produce a file filed under one fixed name.
  /// </remarks>
  public static PixelPowerCollageFile FromRawImage(RawImage image, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);

    return _Encode(image, target.Name);
  }

  /// <summary>
  /// Reduces the picture to one of the three depths the code at 0x40 can name.
  /// </summary>
  /// <remarks>
  /// The choice follows what the picture has: a grey one goes to eight bits, one carrying alpha to
  /// thirty-two, everything else to twenty-four. Both colour depths store blue before red, and the
  /// thirty-two-bit one puts alpha ahead of all three rather than behind them — which is the other
  /// way round from a Windows bitmap of the same depth, and was confirmed against the converter.
  /// </remarks>
  private static PixelPowerCollageFile _Encode(RawImage image, string name) {
    ArgumentNullException.ThrowIfNull(image);

    var (bits, pixels) = image.Format == PixelFormat.Gray8
      ? (8, image.PixelData[..])
      : image.HasAlpha
      ? (32, _ReverseComponents(image.EnsureFormat(PixelFormat.Rgba32).PixelData))
      : (24, image.EnsureFormat(PixelFormat.Bgr24).PixelData[..]);

    return new() {
      Name = name,
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = bits,
      PixelData = pixels,
    };
  }

  /// <summary>
  /// Turns the thirty-two-bit layout round, which is the same work in either direction.
  /// </summary>
  /// <remarks>
  /// The file keeps alpha first and then blue, green, red; the rest of the tool keeps red, green,
  /// blue, alpha. Reversing four bytes takes one to the other and back again, so one method serves
  /// reading and writing both — two would be two places for a correction to land in only one.
  /// <para/>
  /// Not the order the rest of the tool uses — a thirty-two-bit Windows bitmap handed to the same
  /// converter comes out blue, green, red, alpha, and this comes out the other way about. Read as a
  /// Windows bitmap the picture keeps its green and swaps red for blue while the alpha becomes the
  /// blue channel, which on a still with a soft edge is a coloured fringe rather than an obvious fault.
  /// </remarks>
  private static byte[] _ReverseComponents(byte[] pixels) {
    var turned = new byte[pixels.Length];
    for (var at = 0; at + 3 < pixels.Length; at += 4) {
      turned[at] = pixels[at + 3];
      turned[at + 1] = pixels[at + 2];
      turned[at + 2] = pixels[at + 1];
      turned[at + 3] = pixels[at];
    }

    return turned;
  }
}
