using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace FileFormat.PicturePublisher;

/// <summary>Reads Micrografx Picture Publisher 5 (.pp5) documents.</summary>
/// <remarks>
/// Behind a 48-byte header the document is one flat chain of records — four bytes of payload length,
/// two of a type, then the payload. Three types matter: a 106-byte object header naming an object
/// and stating where it sits, a colour raster, and an eight-bit mask for the raster in front of it.
/// <para/>
/// Each raster is a little-endian TIFF of its own with no strip byte count in it, so a strip runs to
/// the end of the record that holds it, and the compression tag is 213 — which is plain zlib rather
/// than anything TIFF defines. The inflated length has to be exactly the width times the height
/// times the samples, which is what tells a real record from a coincidence.
/// <para/>
/// The chain has to consume the file to the byte and every object's stated rectangle has to be the
/// size of its own raster. Both held exactly on the one sample available.
/// </remarks>
public static class PicturePublisherReader {

  /// <summary>An object header: a name, a rectangle on the canvas and an opacity.</summary>
  private const int _RecordObjectHeader = 1;

  /// <summary>A colour raster, three samples of eight bits.</summary>
  private const int _RecordImage = 2;

  /// <summary>An eight-bit mask for the raster in front of it.</summary>
  private const int _RecordMask = 3;

  /// <summary>Payload length and type.</summary>
  private const int _RecordHeaderSize = 6;

  /// <summary>The object header is fixed, and the fields read from it end well inside it.</summary>
  private const int _ObjectHeaderSize = 106;

  /// <summary>Offsets into an object header payload.</summary>
  private const int _ObjectLeft = 38;
  private const int _ObjectTop = 42;
  private const int _ObjectRight = 46;
  private const int _ObjectBottom = 50;
  private const int _ObjectOpacity = 54;

  /// <summary>Micrografx's own compression tag: the strip is a zlib stream.</summary>
  private const int _CompressionZlib = 213;

  /// <summary>No picture in a document of this age is anywhere near this big.</summary>
  private const int _MaximumDimension = 32768;

  public static PicturePublisherFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture Publisher file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PicturePublisherFile FromStream(Stream stream) {
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

  public static PicturePublisherFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PicturePublisherFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PicturePublisherFile.HeaderSize)
      throw new InvalidDataException(
        $"Picture Publisher data too small: expected at least {PicturePublisherFile.HeaderSize} bytes, "
        + $"got {data.Length}.");

    if (!data[..PicturePublisherFile.Signature.Length].SequenceEqual(PicturePublisherFile.Signature))
      throw new InvalidDataException("Not a Picture Publisher document: it does not open with \"PPUBII\".");

    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[18..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[22..]);
    var resolution = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[26..]);
    var samples = BinaryPrimitives.ReadUInt16LittleEndian(data[30..]);

    if (width is <= 0 or > _MaximumDimension || height is <= 0 or > _MaximumDimension)
      throw new InvalidDataException(
        $"Picture Publisher canvas is stated as {width} by {height}, which is not a picture size.");

    if (samples != 3)
      throw new InvalidDataException(
        $"Picture Publisher document states {samples} samples a pixel; only three-sample colour has "
        + "been checked against a file.");

    // Every object goes onto a white page. The base image covers the canvas in the one sample here,
    // so nothing of this shows — but an object stack that leaves a gap should leave paper, not
    // whatever the buffer happened to hold.
    var canvas = new byte[width * height * 3];
    canvas.AsSpan().Fill(0xFF);

    var objects = 0;
    var rasters = 0;
    var rectangle = (Left: 0, Top: 0, Right: 0, Bottom: 0, Opacity: 0);
    var haveRectangle = false;
    var at = PicturePublisherFile.HeaderSize;

    while (at < data.Length) {
      if (at + _RecordHeaderSize > data.Length)
        throw new InvalidDataException(
          $"Picture Publisher record chain runs off the end: {data.Length - at} bytes left at offset "
          + $"{at}, which is less than the {_RecordHeaderSize}-byte record header.");

      var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
      var type = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]);
      var payloadAt = at + _RecordHeaderSize;

      if (length < 0 || payloadAt + length > data.Length)
        throw new InvalidDataException(
          $"Picture Publisher record at offset {at} states a payload of {length} bytes, which reaches "
          + $"past the end of the {data.Length}-byte file.");

      var payload = data.Slice(payloadAt, length);

      switch (type) {
        case _RecordObjectHeader: {
          if (length < _ObjectHeaderSize)
            throw new InvalidDataException(
              $"Picture Publisher object header at offset {at} is {length} bytes where it takes "
              + $"{_ObjectHeaderSize}.");

          rectangle = (
            BinaryPrimitives.ReadInt32LittleEndian(payload[_ObjectLeft..]),
            BinaryPrimitives.ReadInt32LittleEndian(payload[_ObjectTop..]),
            BinaryPrimitives.ReadInt32LittleEndian(payload[_ObjectRight..]),
            BinaryPrimitives.ReadInt32LittleEndian(payload[_ObjectBottom..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[_ObjectOpacity..]));

          if (rectangle.Opacity is < 0 or > 255)
            throw new InvalidDataException(
              $"Picture Publisher object at offset {at} states an opacity of {rectangle.Opacity}, "
              + "which is not a value between nothing and 255.");

          haveRectangle = true;
          ++objects;
          break;
        }

        case _RecordImage: {
          if (!haveRectangle)
            throw new InvalidDataException(
              $"Picture Publisher raster at offset {at} stands in front of no object header, so "
              + "nothing states where on the canvas it goes.");

          var (imageWidth, imageHeight, imageSamples, pixels) = _ReadRaster(payload, at);
          if (imageSamples != 3)
            throw new InvalidDataException(
              $"Picture Publisher raster at offset {at} has {imageSamples} samples a pixel where a "
              + "colour object has three.");

          var stated = (rectangle.Right - rectangle.Left + 1, rectangle.Bottom - rectangle.Top + 1);
          if (stated != (imageWidth, imageHeight))
            throw new InvalidDataException(
              $"Picture Publisher object at offset {at} states a rectangle of {stated.Item1} by "
              + $"{stated.Item2} in front of a raster of {imageWidth} by {imageHeight}.");

          byte[]? mask = null;
          var next = payloadAt + length;
          if (_PeekType(data, next) == _RecordMask) {
            var maskLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[next..]);
            if (next + _RecordHeaderSize + maskLength > data.Length)
              throw new InvalidDataException(
                $"Picture Publisher mask at offset {next} states a payload of {maskLength} bytes, "
                + $"which reaches past the end of the {data.Length}-byte file.");

            var (maskWidth, maskHeight, maskSamples, maskPixels) =
              _ReadRaster(data.Slice(next + _RecordHeaderSize, maskLength), next);

            if (maskSamples != 1 || maskWidth != imageWidth || maskHeight != imageHeight)
              throw new InvalidDataException(
                $"Picture Publisher mask at offset {next} is {maskWidth} by {maskHeight} in "
                + $"{maskSamples} samples, where its object is {imageWidth} by {imageHeight} in one.");

            mask = maskPixels;
            at = next + _RecordHeaderSize + maskLength;
          } else
            at = next;

          _Composite(canvas, width, height, pixels, mask, imageWidth, imageHeight,
            rectangle.Left, rectangle.Top, rectangle.Opacity);

          ++rasters;
          haveRectangle = false;
          continue;
        }

        case _RecordMask:
          throw new InvalidDataException(
            $"Picture Publisher mask at offset {at} stands behind no raster of its own.");
      }

      at = payloadAt + length;
    }

    if (rasters == 0)
      throw new InvalidDataException("Not a Picture Publisher document: its record chain holds no raster.");

    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      ObjectCount = objects,
      PixelData = canvas,
    };
  }

  /// <summary>The type of the record at an offset, or -1 where there is no record there.</summary>
  private static int _PeekType(ReadOnlySpan<byte> data, int at)
    => at + _RecordHeaderSize > data.Length ? -1 : BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]);

  /// <summary>Reads one record's cut-down TIFF and inflates its single strip.</summary>
  private static (int Width, int Height, int Samples, byte[] Pixels) _ReadRaster(ReadOnlySpan<byte> payload, int at) {

    if (payload.Length < 8 || payload[0] != 'I' || payload[1] != 'I' || payload[2] != 0x2A || payload[3] != 0)
      throw new InvalidDataException(
        $"Picture Publisher raster at offset {at} does not open with a little-endian TIFF header.");

    var directoryAt = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
    if (directoryAt < 8 || directoryAt + 2 > payload.Length)
      throw new InvalidDataException(
        $"Picture Publisher raster at offset {at} puts its directory at {directoryAt} of "
        + $"{payload.Length} bytes.");

    var entries = BinaryPrimitives.ReadUInt16LittleEndian(payload[directoryAt..]);
    if (directoryAt + 2 + entries * 12 + 4 > payload.Length)
      throw new InvalidDataException(
        $"Picture Publisher raster at offset {at} states {entries} directory entries, which do not "
        + $"fit in its {payload.Length} bytes.");

    var width = 0;
    var height = 0;
    var samples = 1;
    var compression = 0;
    var stripAt = -1;
    var bitsAt = -1;
    var bitsCount = 0;
    var planar = 1;
    var predictor = 1;

    for (var i = 0; i < entries; ++i) {
      var entryAt = directoryAt + 2 + i * 12;
      var tag = BinaryPrimitives.ReadUInt16LittleEndian(payload[entryAt..]);
      var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[(entryAt + 4)..]);
      var value = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[(entryAt + 8)..]);
      var inlineShort = BinaryPrimitives.ReadUInt16LittleEndian(payload[(entryAt + 8)..]);

      switch (tag) {
        case 256: width = value; break;
        case 257: height = value; break;
        case 258:
          bitsCount = count;
          if (count == 1)
            bitsAt = -1;
          else
            bitsAt = value;

          if (count == 1 && inlineShort != 8)
            throw new InvalidDataException(
              $"Picture Publisher raster at offset {at} states {inlineShort} bits a sample where "
              + "eight is the only depth checked against a file.");
          break;
        case 259: compression = inlineShort; break;
        case 273: stripAt = value; break;
        case 277: samples = inlineShort; break;
        case 284: planar = inlineShort; break;
        case 317: predictor = inlineShort; break;
      }
    }

    if (compression != _CompressionZlib)
      throw new InvalidDataException(
        $"Picture Publisher raster at offset {at} states compression {compression}; only "
        + $"{_CompressionZlib}, which is a zlib stream, has been checked against a file.");

    if (planar != 1)
      throw new InvalidDataException(
        $"Picture Publisher raster at offset {at} stores its samples in {planar} planes; only one "
        + "interleaved plane has been checked against a file.");

    if (predictor != 1)
      throw new InvalidDataException(
        $"Picture Publisher raster at offset {at} states predictor {predictor}; only unpredicted "
        + "data has been checked against a file.");

    if (width is <= 0 or > _MaximumDimension || height is <= 0 or > _MaximumDimension)
      throw new InvalidDataException(
        $"Picture Publisher raster at offset {at} is stated as {width} by {height}, which is not a "
        + "picture size.");

    if (samples is not 1 and not 3)
      throw new InvalidDataException(
        $"Picture Publisher raster at offset {at} states {samples} samples a pixel; only one and "
        + "three have been checked against a file.");

    if (bitsAt >= 0) {
      if (bitsCount != samples || bitsAt + bitsCount * 2 > payload.Length)
        throw new InvalidDataException(
          $"Picture Publisher raster at offset {at} states {bitsCount} sample depths for {samples} "
          + "samples, or puts them past its own end.");

      for (var i = 0; i < bitsCount; ++i)
        if (BinaryPrimitives.ReadUInt16LittleEndian(payload[(bitsAt + i * 2)..]) != 8)
          throw new InvalidDataException(
            $"Picture Publisher raster at offset {at} states a sample depth other than eight, which "
            + "has not been checked against a file.");
    }

    if (stripAt < 0 || stripAt >= payload.Length)
      throw new InvalidDataException(
        $"Picture Publisher raster at offset {at} puts its strip at {stripAt} of {payload.Length} bytes.");

    // There is no strip byte count in these directories, so the strip is everything the record has
    // left. That is why the record's own length is what bounds the read.
    var expected = width * height * samples;
    var pixels = _Inflate(payload[stripAt..], expected, at);

    return (width, height, samples, pixels);
  }

  /// <summary>Inflates a strip, refusing anything that does not come out at exactly the stated size.</summary>
  private static byte[] _Inflate(ReadOnlySpan<byte> compressed, int expected, int at) {
    var output = new byte[expected];

    using var source = new MemoryStream(compressed.ToArray(), writable: false);
    using var inflate = new ZLibStream(source, CompressionMode.Decompress);

    var filled = 0;
    while (filled < expected) {
      int read;
      try {
        read = inflate.Read(output, filled, expected - filled);
      } catch (InvalidDataException) {
        throw new InvalidDataException(
          $"Picture Publisher strip at offset {at} is not a zlib stream, though the directory says "
          + "it is.");
      }

      if (read <= 0)
        break;

      filled += read;
    }

    if (filled != expected)
      throw new InvalidDataException(
        $"Picture Publisher strip at offset {at} inflates to {filled} bytes where its directory's "
        + $"size needs {expected}.");

    return output;
  }

  /// <summary>Lays one object over the canvas, through its mask and its opacity.</summary>
  private static void _Composite(
    byte[] canvas, int canvasWidth, int canvasHeight,
    byte[] pixels, byte[]? mask, int width, int height,
    int left, int top, int opacity) {

    for (var y = 0; y < height; ++y) {
      var canvasY = top + y;
      if (canvasY < 0 || canvasY >= canvasHeight)
        continue;

      for (var x = 0; x < width; ++x) {
        var canvasX = left + x;
        if (canvasX < 0 || canvasX >= canvasWidth)
          continue;

        var alpha = opacity;
        if (mask is not null)
          alpha = alpha * mask[y * width + x] / 255;

        if (alpha <= 0)
          continue;

        var source = (y * width + x) * 3;
        var target = (canvasY * canvasWidth + canvasX) * 3;
        for (var channel = 0; channel < 3; ++channel)
          canvas[target + channel] =
            (byte)((pixels[source + channel] * alpha + canvas[target + channel] * (255 - alpha)) / 255);
      }
    }
  }
}
