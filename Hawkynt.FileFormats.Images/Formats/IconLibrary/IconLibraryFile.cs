using System;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.IconLibrary;

/// <summary>In-memory representation of a Windows Icon Library (ICL) container.</summary>
public readonly record struct IconLibraryFile
  : IImageFormatReader<IconLibraryFile>, IImageToRawImage<IconLibraryFile>,
    IImageFromRawImage<IconLibraryFile>, IImageFormatWriter<IconLibraryFile> {

  /// <summary>Default icon dimensions when not detectable.</summary>
  internal const int DefaultSize = 32;

  /// <summary>
  /// Present because <see cref="RawData"/> carries a field initializer, which a record struct may
  /// only have alongside an explicitly declared constructor.
  /// </summary>
  public IconLibraryFile() { }

  static string IImageFormatMetadata<IconLibraryFile>.PrimaryExtension => ".icl";
  static string[] IImageFormatMetadata<IconLibraryFile>.FileExtensions => [".icl"];
  static IconLibraryFile IImageFormatReader<IconLibraryFile>.FromSpan(ReadOnlySpan<byte> data) => IconLibraryReader.FromSpan(data);
  static byte[] IImageFormatWriter<IconLibraryFile>.ToBytes(IconLibraryFile file) => IconLibraryWriter.ToBytes(file);

  /// <summary>Icon width (default 32).</summary>
  public int Width { get; init; }

  /// <summary>Icon height (default 32).</summary>
  public int Height { get; init; }

  /// <summary>Raw file data, retained when a library was read rather than created.</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>The icon to place in a newly created resource-only PE library.</summary>
  internal IcoImage? EncodedImage { get; init; }

  /// <summary>Builds a one-icon Windows icon library from an arbitrary image.</summary>
  public static IconLibraryFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var icon = IcoDib.FromRawImage(image);
    return new() {
      Width = icon.Width,
      Height = icon.Height,
      EncodedImage = icon,
    };
  }

  /// <summary>
  /// Refuses the file, the icons inside one of these not being read here.
  /// </summary>
  /// <remarks>
  /// An icon library is an executable carrying icons as resources, and pulling them out means
  /// walking its resource tables. That is not done here; what was returned instead was a picture of
  /// the right size with every pixel black, which counts as a decode and cannot be told from one.
  /// A picture that is the right shape and entirely wrong is worse than none, because nothing
  /// downstream has any way to notice.
  /// </remarks>
  public static RawImage ToRawImage(IconLibraryFile file)
    => throw new NotSupportedException("The icons inside an icon library are not read here; only the file itself is recognised.");

}
