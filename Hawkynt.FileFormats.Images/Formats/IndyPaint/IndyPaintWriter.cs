using System;
using System.Buffers.Binary;

namespace FileFormat.IndyPaint;

/// <summary>Assembles IndyPaint screen dump bytes from pixel data.</summary>
/// <remarks>
/// The size goes in the header rather than being assumed: this used to write 320 by 240 into every
/// file whatever the picture was, so a 384-wide one came out claiming to be narrower than it is.
/// </remarks>
public static class IndyPaintWriter {

  public static byte[] ToBytes(IndyPaintFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    if (width < 1) width = IndyPaintFile.DefaultWidth;
    if (height < 1) height = IndyPaintFile.DefaultHeight;

    var pixelBytes = width * height * IndyPaintFile.BytesPerPixel;
    var result = new byte[IndyPaintFile.HeaderSize + pixelBytes];

    IndyPaintFile.Signature.CopyTo(result);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(IndyPaintFile.DimensionsOffset), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(IndyPaintFile.DimensionsOffset + 2), (ushort)height);

    (pixelData ?? []).AsSpan(0, Math.Min((pixelData ?? []).Length, pixelBytes))
      .CopyTo(result.AsSpan(IndyPaintFile.HeaderSize));

    return result;
  }
}
