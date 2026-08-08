using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Cgm;

/// <summary>A Computer Graphics Metafile (.cgm) in the binary encoding of ISO/IEC 8632-3.</summary>
/// <remarks>
/// A flat stream of commands, each opening with one sixteen-bit word: four bits of element class,
/// seven of element identifier and five of parameter length, with the length escaping to a second
/// word when it will not fit in five bits. Every command begins on a word boundary, so an odd
/// parameter list is followed by one pad byte that the stated length does not count.
/// <para/>
/// What the parameters mean depends on precisions the file sets as it goes — how many bits an
/// integer takes, whether a real is fixed or floating point, how wide a colour index is — so the
/// stream cannot be read out of order and a precision misread desynchronises everything after it.
/// That is also what makes it verifiable: a file read with the wrong precisions does not land on
/// its own end-of-metafile command, and every one of these does.
/// <para/>
/// The picture is drawn at the extent the file states for it, which is what the VDC extent element
/// is for. Where that extent is stated in abstract units rather than physical ones, one unit is one
/// pixel, capped; where the file states a metric scale factor, that is a physical size and it is
/// taken at ninety-six pixels to the inch.
/// <para/>
/// The other two encodings the standard defines — the character encoding and the clear-text one —
/// are refused rather than half read. They are different grammars, not variations, and a file in
/// either of them opens with something this would misread.
/// <para/>
/// Text is not drawn: the fonts a metafile names are not in it. It does not write.
/// </remarks>
public readonly record struct CgmFile : IImageFormatReader<CgmFile>, IImageToRawImage<CgmFile> {

  static string IImageFormatMetadata<CgmFile>.PrimaryExtension => ".cgm";
  static string[] IImageFormatMetadata<CgmFile>.FileExtensions => [".cgm"];
  static CgmFile IImageFormatReader<CgmFile>.FromSpan(ReadOnlySpan<byte> data) => CgmReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<CgmFile>.VideoModes => [
    new("Picture", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<CgmFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2)
      return null;

    // Every binary metafile opens with BEGIN METAFILE: class 0, identifier 1, which is the top
    // eleven bits of the first word and leaves only the length free.
    var word = (header[0] << 8) | header[1];
    return (word & 0xFFE0) == 0x0020;
  }

  /// <summary>Everything the file draws, in the order it draws it.</summary>
  public IReadOnlyList<CgmCommand> Commands { get; init; }

  /// <summary>What the file calls itself, out of its BEGIN METAFILE command.</summary>
  public string? Name { get; init; }

  public static RawImage ToRawImage(CgmFile file) => CgmRenderer.Render(file);
}

/// <summary>One command: what it is and the bytes of its parameter list.</summary>
/// <param name="ElementClass">Which of the standard's classes the command belongs to.</param>
/// <param name="ElementId">Which command within that class.</param>
/// <param name="Parameters">The parameter list, with the partitions joined and the padding dropped.</param>
public readonly record struct CgmCommand(int ElementClass, int ElementId, byte[] Parameters);
