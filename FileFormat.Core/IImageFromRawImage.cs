using System;
using System.IO;

namespace FileFormat.Core;

/// <summary>Creates the in-memory file representation from a platform-independent <see cref="RawImage"/>. Optional — read-only formats omit this.</summary>
public interface IImageFromRawImage<TSelf> where TSelf : IImageFromRawImage<TSelf> {

  /// <summary>Creates the in-memory representation from a platform-independent <see cref="RawImage"/>.</summary>
  static abstract TSelf FromRawImage(RawImage image);

  /// <summary>Creates the representation for a file about to be given the stated extension.</summary>
  /// <param name="image">The picture to encode.</param>
  /// <param name="extension">The extension the file will carry, leading dot included.</param>
  /// <remarks>
  /// A few families share one layout across several extensions, the extension being the only thing
  /// that says which variant a file is — their readers already take it. Encoding had nowhere to put
  /// it, so those writers always emitted the default variant: bytes written under any other
  /// extension in the family were then read back by every tool, ours included, in the wrong mode.
  /// <para/>
  /// The default ignores it, which is right for the formats where the extension is only a name.
  /// </remarks>
  static virtual TSelf FromRawImage(RawImage image, string extension) => TSelf.FromRawImage(image);

  /// <summary>Creates the representation for the file about to be written at this path.</summary>
  /// <param name="image">The picture to encode.</param>
  /// <param name="target">Where the bytes are going.</param>
  /// <remarks>
  /// The same argument as the overload above, one step further along. At least one format writes the
  /// name it must be filed under INTO the file — Pixel Power Collage keeps it in the first thirty-two
  /// bytes and refuses to open a file whose name has changed — so the extension is not enough and the
  /// whole name is wanted. Encoding still has nowhere to put that, and a writer with nowhere to put it
  /// can only emit a file that refuses to open, which is what left the format read-only.
  /// <para/>
  /// Only the write that names a file goes this way, so the byte-array route still gets whatever the
  /// format can build without a path. The default ignores the path beyond its extension, which is
  /// right for everything that does not name itself.
  /// </remarks>
  static virtual TSelf FromRawImage(RawImage image, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);

    return TSelf.FromRawImage(image, target.Extension);
  }
}
