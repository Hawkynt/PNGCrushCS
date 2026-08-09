using System;
using FileFormat.Core;

namespace FileFormat.Vrml;

/// <summary>In-memory representation of the picture carried inside a VRML 2.0 scene.</summary>
/// <remarks>
/// VRML describes a three-dimensional scene rather than a picture, and drawing one is a renderer's
/// job. But a scene may carry a bitmap inline, in a <c>PixelTexture</c> node, and that node is a
/// picture in every sense: a width, a height, a number of components, and one integer per pixel.
/// XnView writes exactly that — a unit box wearing the picture as its texture — and cannot read it
/// back, its catalogue row for the name having no loader at all. So the whole of what it writes is
/// recoverable here and is not recoverable there.
/// <para/>
/// The rows run the way the specification says and the way nothing else here does: the FIRST row of
/// the field is the BOTTOM row of the picture, because a texture's origin is its lower-left corner.
/// Read the other way up a picture comes back mirrored, which on a gradient is not obvious and on a
/// photograph is.
/// <para/>
/// One to four components, meaning grey, grey with alpha, colour, and colour with alpha; each pixel
/// is one integer with the first component in the most significant byte. The count of integers has
/// to come to exactly the width times the height — that is the whole of the check that this really
/// is a picture, there being no length field anywhere to compare against.
/// </remarks>
[FormatMagicBytes([0x23, 0x56, 0x52, 0x4D, 0x4C])] // "#VRML"
[FormatMimeType("model/vrml", "x-world/x-vrml")]
public readonly record struct VrmlFile : IImageFormatReader<VrmlFile>, IImageToRawImage<VrmlFile>, IImageFromRawImage<VrmlFile>, IImageFormatWriter<VrmlFile> {

  static string IImageFormatMetadata<VrmlFile>.PrimaryExtension => ".wrl";
  static string[] IImageFormatMetadata<VrmlFile>.FileExtensions => [".wrl", ".vrml"];
  static VrmlFile IImageFormatReader<VrmlFile>.FromSpan(ReadOnlySpan<byte> data) => VrmlReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<VrmlFile>.VideoModes => [new("PixelTexture", [(IntegerRange.Any, IntegerRange.Any)])];
  static byte[] IImageFormatWriter<VrmlFile>.ToBytes(VrmlFile file) => VrmlWriter.ToBytes(file);

  /// <summary>The header every VRML 2.0 file opens with, ahead of any node.</summary>
  public const string Header = "#VRML V2.0";

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>One for grey, two for grey and alpha, three for colour, four for colour and alpha.</summary>
  public int Components { get; init; }

  /// <summary>The samples, already turned the right way up, one row after another from the top.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(VrmlFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = file.Components switch {
      1 => PixelFormat.Gray8,
      2 => PixelFormat.GrayAlpha16,
      3 => PixelFormat.Rgb24,
      _ => PixelFormat.Rgba32,
    },
    PixelData = file.PixelData[..],
  };

  /// <summary>
  /// Turns a picture into a texture: grey, colour, or colour with alpha, and nothing between.
  /// </summary>
  /// <remarks>
  /// The field allows two components as well, and this never writes them — which is deliberate rather
  /// than an omission. XnView's converter, the only producer these files were checked against, turns a
  /// grey-with-alpha picture into four components and never emits two; matching it keeps our output
  /// inside the shapes that were actually confirmed. The reader still takes two from anything else
  /// that writes them.
  /// <para/>
  /// Anything carrying alpha goes to four components rather than being flattened to three, which is
  /// what the same converter does with grey-and-alpha. Choosing by the picture's own layout instead
  /// would drop the alpha of every picture that keeps it blue-first.
  /// </remarks>
  public static VrmlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.HasAlpha
      ? image.EnsureFormat(PixelFormat.Rgba32)
      : image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);

    return new() {
      Width = source.Width,
      Height = source.Height,
      Components = source.Format switch {
        PixelFormat.Gray8 => 1,
        PixelFormat.Rgba32 => 4,
        _ => 3,
      },
      PixelData = source.PixelData[..],
    };
  }
}
