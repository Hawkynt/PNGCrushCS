using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.IconLibrary;

/// <summary>In-memory representation of a Windows Icon Library (ICL) container.</summary>
/// <remarks>
/// An ICL is a resource-only Windows library. Each logical icon is an <c>RT_GROUP_ICON</c> resource
/// whose entries point at one or more <c>RT_ICON</c> image resources. Both PE (Win32) and legacy NE
/// (Win16) libraries are accepted; newly encoded libraries are PE resource-only DLLs.
/// </remarks>
public readonly record struct IconLibraryFile
  : IImageFormatReader<IconLibraryFile>, IImageToRawImage<IconLibraryFile>, IImageFromRawImage<IconLibraryFile>,
    IImageFormatWriter<IconLibraryFile>, IMultiImageFileFormat<IconLibraryFile> {

  /// <summary>Default icon dimensions when no image is available.</summary>
  internal const int DefaultSize = 32;

  /// <summary>
  /// Present because collection fields carry initializers, which a record struct may only have
  /// alongside an explicitly declared constructor.
  /// </summary>
  public IconLibraryFile() { }

  static string IImageFormatMetadata<IconLibraryFile>.PrimaryExtension => ".icl";
  static string[] IImageFormatMetadata<IconLibraryFile>.FileExtensions => [".icl"];
  static FormatCapability IImageFormatMetadata<IconLibraryFile>.Capabilities => FormatCapability.MultiImage;
  static IconLibraryFile IImageFormatReader<IconLibraryFile>.FromSpan(ReadOnlySpan<byte> data) => IconLibraryReader.FromSpan(data);
  static byte[] IImageFormatWriter<IconLibraryFile>.ToBytes(IconLibraryFile file) => IconLibraryWriter.ToBytes(file);

  /// <summary>Width of the preferred image in the first logical icon.</summary>
  public int Width { get; init; }

  /// <summary>Height of the preferred image in the first logical icon.</summary>
  public int Height { get; init; }

  /// <summary>
  /// Logical icons in resource-directory order. Each <see cref="IcoFile"/> contains that icon's
  /// size/colour-depth variants.
  /// </summary>
  public IReadOnlyList<IcoFile> Icons { get; init; } = [];

  /// <summary>
  /// Original library bytes. Parsed files retain these so writing an untouched file preserves
  /// resource identifiers, names, languages, and unrelated executable metadata exactly.
  /// </summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>The image used when this library was created from a <see cref="RawImage"/>.</summary>
  internal IcoImage? EncodedImage { get; init; }

  /// <summary>Builds a one-icon Windows icon library from an arbitrary image.</summary>
  public static IconLibraryFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var iconImage = IcoDib.FromRawImage(image);
    return new() {
      Width = iconImage.Width,
      Height = iconImage.Height,
      Icons = [new IcoFile { Images = [iconImage] }],
      EncodedImage = iconImage,
    };
  }

  /// <summary>Returns the number of logical icons in the library.</summary>
  public static int ImageCount(IconLibraryFile file) => file.Icons.Count;

  /// <summary>Returns the preferred representation of the first logical icon.</summary>
  public static RawImage ToRawImage(IconLibraryFile file) {
    if (file.Icons.Count == 0)
      throw new ArgumentException("Icon Library contains no icon groups.", nameof(file));

    return IcoFile.ToRawImage(file.Icons[0]);
  }

  /// <summary>Returns the preferred representation of the logical icon at <paramref name="index"/>.</summary>
  public static RawImage ToRawImage(IconLibraryFile file, int index) {
    if ((uint)index >= (uint)file.Icons.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return IcoFile.ToRawImage(file.Icons[index]);
  }
}
