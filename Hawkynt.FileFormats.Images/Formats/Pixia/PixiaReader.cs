using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Pixia;

/// <summary>Reads Pixia pictures from bytes, streams, or file paths.</summary>
public static class PixiaReader {

  /// <summary>The three bytes a JFIF opens with.</summary>
  private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

  /// <summary>One layer as the file stores it, before anything is composited.</summary>
  private sealed record Layer(int Width, int Height, int X, int Y, bool Visible, byte[] Colours, byte[][] Planes);

  public static PixiaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Pixia picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PixiaFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static PixiaFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PixiaFile.PreviewAt)
      throw new InvalidDataException($"Data too small for a Pixia picture (got {data.Length} bytes).");

    if (!data[..PixiaFile.Signature.Length].SequenceEqual(Encoding.ASCII.GetBytes(PixiaFile.Signature)))
      throw new InvalidDataException("Not a Pixia picture: it does not open with its name.");

    var version = BinaryPrimitives.ReadInt32LittleEndian(data[PixiaFile.VersionAt..]);
    var layerCount = BinaryPrimitives.ReadInt32LittleEndian(data[PixiaFile.LayerCountAt..]);

    if (layerCount < 1 || layerCount > PixiaFile.MaximumLayers)
      throw new InvalidDataException($"A Pixia picture states {layerCount} layers, and the tables hold {PixiaFile.MaximumLayers}.");

    // Version 1 stores its rows uncompressed under a different layout, and one sample is not enough
    // to state where it keeps them. Refusing it is better than drawing a guess.
    var previewLength = BinaryPrimitives.ReadInt32LittleEndian(data[PixiaFile.HeaderSize..]);
    if (data.Length < PixiaFile.PreviewAt + JpegSignature.Length
        || !data.Slice(PixiaFile.PreviewAt, JpegSignature.Length).SequenceEqual(JpegSignature))
      throw new InvalidDataException($"A Pixia picture of version {version} stores its layers uncompressed, which this does not read.");

    if (previewLength < 0 || PixiaFile.PreviewAt + (long)previewLength > data.Length)
      throw new InvalidDataException($"The Pixia header states a preview of {previewLength} bytes in a file of {data.Length}.");

    var layers = new Layer[layerCount];
    var at = PixiaFile.PreviewAt + previewLength;

    for (var i = 0; i < layerCount; ++i) {
      var geometry = PixiaFile.GeometryAt + i * PixiaFile.GeometryEntrySize;
      var width = BinaryPrimitives.ReadInt32LittleEndian(data[geometry..]);
      var height = BinaryPrimitives.ReadInt32LittleEndian(data[(geometry + 4)..]);

      if (width < 1 || height < 1)
        throw new InvalidDataException($"Invalid Pixia layer size: {width}x{height}.");

      if (at + PixiaFile.LayerRecordSize > data.Length)
        throw new InvalidDataException($"The Pixia layer table states {layerCount} layers and the file runs out at layer {i}.");

      var planeCount = data[at];
      at += PixiaFile.LayerRecordSize;

      // Every plane holds one row more than the layer is tall; the extra is a copy of the last row.
      var stored = width * (height + 1);
      var colours = _ReadRuns(data, ref at, stored * 3, 3);

      var planes = new byte[planeCount][];
      for (var k = 0; k < planeCount; ++k)
        planes[k] = _ReadRuns(data, ref at, stored, 1);

      var property = PixiaFile.PropertiesAt + i * PixiaFile.PropertyEntrySize;
      layers[i] = new(
        width, height,
        BinaryPrimitives.ReadInt32LittleEndian(data[(property + PixiaFile.PropertyXAt)..]),
        BinaryPrimitives.ReadInt32LittleEndian(data[(property + PixiaFile.PropertyYAt)..]),
        BinaryPrimitives.ReadInt32LittleEndian(data[(property + PixiaFile.PropertyVisibleAt)..]) != 0,
        colours, planes);
    }

    // The layers run to the end of the file. That is what says the runs were read as the format
    // means them rather than merely as far as something plausible.
    if (at != data.Length)
      throw new InvalidDataException($"The Pixia layers account for {at} bytes and the file is {data.Length}.");

    var canvasWidth = layers[0].Width;
    var canvasHeight = layers[0].Height;

    return new() {
      Width = canvasWidth,
      Height = canvasHeight,
      Version = version,
      LayerCount = layerCount,
      Preview = data.Slice(PixiaFile.PreviewAt, previewLength).ToArray(),
      PixelData = _Composite(layers, canvasWidth, canvasHeight, version),
    };
  }

  /// <summary>Expands one list of runs, each a count and that many bytes' worth of one value.</summary>
  private static byte[] _ReadRuns(ReadOnlySpan<byte> data, ref int at, int expected, int channels) {
    var result = new byte[expected];
    var written = 0;
    var packet = channels + 1;

    while (true) {
      if (at + packet > data.Length)
        throw new InvalidDataException("A Pixia layer's runs end before the count that terminates them.");

      var count = data[at];
      if (count == PixiaFile.RunTerminator) {
        at += packet;
        break;
      }

      if (written + count * channels > expected)
        throw new InvalidDataException($"A Pixia layer's runs expand past the {expected} bytes its size accounts for.");

      for (var n = 0; n < count; ++n)
        for (var c = 0; c < channels; ++c)
          result[written++] = data[at + 1 + c];

      at += packet;
    }

    if (written != expected)
      throw new InvalidDataException($"A Pixia layer's runs expand to {written} bytes and its size accounts for {expected}.");

    return result;
  }

  /// <summary>Lays the layers over white, bottom to top, at the offsets the properties give.</summary>
  private static byte[] _Composite(Layer[] layers, int width, int height, int version) {
    var canvas = new byte[width * height * 3];
    Array.Fill(canvas, (byte)0xFF);

    // One plane is the opacity and the rest are inverted masks; which one follows the version.
    var opacityIndex = version >= PixiaFile.FirstVersionWithLeadingOpacity ? 0 : 1;

    foreach (var layer in layers) {
      if (!layer.Visible || layer.Planes.Length <= opacityIndex)
        continue;

      var opacity = layer.Planes[opacityIndex];

      for (var y = 0; y < layer.Height; ++y) {
        var destinationY = y + layer.Y;
        if (destinationY < 0 || destinationY >= height)
          continue;

        for (var x = 0; x < layer.Width; ++x) {
          var destinationX = x + layer.X;
          if (destinationX < 0 || destinationX >= width)
            continue;

          var source = y * layer.Width + x;

          var alpha = (int)opacity[source];
          for (var k = 0; k < layer.Planes.Length; ++k)
            if (k != opacityIndex)
              alpha = alpha * (255 - layer.Planes[k][source]) / 255;

          if (alpha == 0)
            continue;

          var from = source * 3;
          var to = (destinationY * width + destinationX) * 3;
          var inverse = 255 - alpha;

          // Stored blue, green, red.
          canvas[to] = (byte)((canvas[to] * inverse + layer.Colours[from + 2] * alpha) / 255);
          canvas[to + 1] = (byte)((canvas[to + 1] * inverse + layer.Colours[from + 1] * alpha) / 255);
          canvas[to + 2] = (byte)((canvas[to + 2] * inverse + layer.Colours[from] * alpha) / 255);
        }
      }
    }

    return canvas;
  }

  public static PixiaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
