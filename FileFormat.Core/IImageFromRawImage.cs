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
}
