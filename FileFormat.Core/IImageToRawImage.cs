namespace FileFormat.Core;

/// <summary>Converts the in-memory file representation to a platform-independent <see cref="RawImage"/>.</summary>
public interface IImageToRawImage<TSelf> where TSelf : IImageToRawImage<TSelf> {

  /// <summary>Converts the in-memory representation to a platform-independent <see cref="RawImage"/>.</summary>
  static abstract RawImage ToRawImage(TSelf file);
}
