using System;
using FileFormat.Mapletown;

namespace FileFormat.MapletownMl1;

/// <summary>Assembles Mapletown Network ML1 picture bytes from a <see cref="MapletownMl1File"/>.</summary>
/// <remarks>
/// The same image MX1 carries, written straight out as bytes instead of being repacked into the
/// printable alphabet a bulletin board would pass. There is no announcing line either: an ML1 file
/// is the bit stream and nothing else, which is why it opens on the signature rather than on text.
/// <para/>
/// What is written is a picture, not a drawing. The format's own runs and chains describe strokes
/// laid down in a paint program, and a chain is worth writing only where a shape's outline is known
/// before its fill; reduced from pixels there is no such outline, so every pixel is covered by a
/// plain horizontal run and the chain bit is always zero. The file that comes out is longer than the
/// drawing program would have made of the same picture and reads back identically, which is the
/// trade a serialiser is allowed to make and a re-drawing is not.
/// </remarks>
public static class MapletownMl1Writer {

  public static byte[] ToBytes(MapletownMl1File file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(file));

    // The picture states its corners in sixteen bits apiece, so a wider one has no way to say where
    // it ends.
    if (width > MapletownPicture.MaxDimension || height > MapletownPicture.MaxDimension)
      throw new ArgumentException(
        $"A picture is at most {MapletownPicture.MaxDimension} pixels on a side; "
        + $"this one is {width}x{height}.",
        nameof(file));

    // The end of the picture is announced as a length one past its pixel count, and a length is what
    // it is: twenty-one bits at the widest. A larger picture has no way to say it has finished.
    if ((long)width * height > MapletownPicture.MaxPixels)
      throw new ArgumentException(
        $"A picture is at most {MapletownPicture.MaxPixels} pixels; this one is {(long)width * height}.",
        nameof(file));

    var encoder = MapletownEncoder.Binary();
    MapletownPicture.WriteImage(encoder, width, height, file.Pixels ?? []);

    return encoder.ToArray();
  }
}
