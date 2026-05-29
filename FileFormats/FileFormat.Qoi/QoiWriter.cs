using System;

namespace FileFormat.Qoi;

/// <summary>Assembles QOI file bytes from pixel data.</summary>
public static class QoiWriter {

  public static byte[] ToBytes(QoiFile file) => Assemble(
    file.PixelData,
    file.Width,
    file.Height,
    file.Channels,
    file.ColorSpace
  );

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    QoiChannels channels,
    QoiColorSpace colorSpace
  ) {
    var encoded = QoiCodec.Encode(pixelData, width, height, channels);

    // The encoder already includes the header and end marker
    // Patch the colorspace into the header
    var header = new QoiHeader(
      (byte)'q', (byte)'o', (byte)'i', (byte)'f',
      (uint)width, (uint)height, channels, colorSpace
    );
    header.WriteTo(encoded.AsSpan(0, QoiHeader.StructSize));

    return encoded;
  }
}
