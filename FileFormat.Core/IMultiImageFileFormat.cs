using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>Companion interface for file formats that contain multiple images (e.g. ICO, APNG, DCX, TIFF).</summary>
public interface IMultiImageFileFormat<TSelf> where TSelf : IMultiImageFileFormat<TSelf> {

  /// <summary>Returns the number of images/frames/pages in the file.</summary>
  static abstract int ImageCount(TSelf file);

  /// <summary>Converts a specific image at the given index to a <see cref="RawImage"/>.</summary>
  static abstract RawImage ToRawImage(TSelf file, int index);

  /// <summary>Converts all images to a list of <see cref="RawImage"/> instances.</summary>
  static virtual IReadOnlyList<RawImage> ToRawImages(TSelf file) {
    var count = TSelf.ImageCount(file);
    var result = new RawImage[count];
    for (var i = 0; i < count; ++i)
      result[i] = TSelf.ToRawImage(file, i);
    return result;
  }
}
