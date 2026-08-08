using System;
using System.IO;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Core.Vector;

namespace FileFormat.PostScript;

/// <summary>The operators that lay a raster onto the page.</summary>
/// <remarks>
/// An image in PostScript is a rectangle of samples that fills the unit square of user space, with a
/// matrix saying which way round the samples run inside it. Drawing it is therefore filling that
/// square with a paint whose colour depends on where in the square it is asked — which is exactly
/// what <see cref="VectorPaint"/> is for, so the image goes through the same rasteriser as every
/// other fill and gets the same soft edges and the same clipping for nothing.
/// <para/>
/// The samples are read out of the source as they are needed rather than expanded into a picture
/// first: the page has however many pixels it has, and an image far larger than the space it lands
/// in would otherwise cost memory in proportion to the file rather than to the page.
/// </remarks>
public static class PostScriptImages {

  /// <summary>The most samples one image may carry, which bounds what a wrong number in a file can cost.</summary>
  public const long MaximumSamples = 1L << 28;

  /// <summary>Defines the image operators.</summary>
  public static void Install(PsDictionary system) {
    ArgumentNullException.ThrowIfNull(system);

    PostScriptOperators.Define(system, "image", static i => _Image(i, false));
    PostScriptOperators.Define(system, "imagemask", static i => _Image(i, true));
    PostScriptOperators.Define(system, "colorimage", _ColourImage);
  }

  /// <summary>How a run of samples is laid out and what the numbers in it mean.</summary>
  private sealed class Raster {
    public required int Width;
    public required int Height;
    public required int Bits;
    public required int Components;
    public required PsColourSpace Space;
    public required double[] Decode;
    public required Matrix2D ImageMatrix;
    public required bool IsMask;
    public byte[] Data = [];

    /// <summary>How many bytes one row takes, rows being padded out to whole bytes.</summary>
    public int RowBytes => (int)(((long)this.Width * this.Components * this.Bits + 7) / 8);
  }

  private static void _Image(PostScriptInterpreter interpreter, bool mask) {
    var top = interpreter.Peek();
    var raster = top.Type is PsType.Dictionary
      ? _FromDictionary(interpreter, mask)
      : _FromOperands(interpreter, mask);

    _Draw(interpreter, raster);
  }

  /// <summary>
  /// The Level 1 form, whose operands state the shape of the image and nothing else.
  /// </summary>
  /// <remarks>
  /// <c>width height bits matrix source image</c> for a picture, and
  /// <c>width height polarity matrix source imagemask</c> for a mask, where the polarity says
  /// whether a sample of nought paints or a sample of one does. A picture in this form is always in
  /// the current colour space with one component, which for the files that use it is grey.
  /// </remarks>
  private static Raster _FromOperands(PostScriptInterpreter interpreter, bool mask) {
    var source = interpreter.Pop();
    var matrix = PostScriptGraphicsOperators.ToMatrix(interpreter.PopArray());
    var third = interpreter.Pop();
    var height = (int)interpreter.PopInteger();
    var width = (int)interpreter.PopInteger();

    var invert = false;
    var bits = 8;
    if (mask) {
      if (third.Type != PsType.Boolean)
        throw new PsErrorException("typecheck", $"An imagemask was given {third.TypeName} where its polarity belongs.");

      invert = third.Boolean;
      bits = 1;
    } else {
      if (third.Type != PsType.Integer)
        throw new PsErrorException("typecheck", $"An image was given {third.TypeName} where its sample size belongs.");

      bits = (int)third.Integer;
    }

    var raster = new Raster {
      Width = width,
      Height = height,
      Bits = bits,
      Components = 1,
      Space = mask ? PsColourSpace.Gray : PsColourSpace.Gray,
      Decode = mask ? (invert ? [1, 0] : [0, 1]) : [0, 1],
      ImageMatrix = matrix,
      IsMask = mask
    };

    _Check(raster);
    raster.Data = _Read(interpreter, source, (long)raster.RowBytes * raster.Height);
    return raster;
  }

  /// <summary>
  /// The Level 2 form, whose one operand is a dictionary describing the image.
  /// </summary>
  /// <remarks>
  /// The keys are the ones the reference lists for an image dictionary of type 1: the size, the
  /// matrix, where the samples come from, how many bits each takes and how the numbers map onto
  /// colour. The colour space is whatever <c>setcolorspace</c> last named, which is what says how
  /// many components a sample has.
  /// </remarks>
  private static Raster _FromDictionary(PostScriptInterpreter interpreter, bool mask) {
    var dictionary = interpreter.PopDictionary();

    var type = _Integer(dictionary, "ImageType", 1);
    if (type != 1)
      throw new PsUnsupportedException($"A PostScript image of type {type}, which is not the sampled rectangle this reader draws.");

    var width = _Integer(dictionary, "Width", -1);
    var height = _Integer(dictionary, "Height", -1);
    if (width < 0 || height < 0)
      throw new PsErrorException("rangecheck", "A PostScript image dictionary that does not state its size.");

    if (!dictionary.TryGet("ImageMatrix", out var matrixValue) || matrixValue.Type != PsType.Array)
      throw new PsErrorException("rangecheck", "A PostScript image dictionary with no matrix.");

    var isMask = mask || (dictionary.TryGet("ImageMask", out var maskValue) && maskValue.Type == PsType.Boolean && maskValue.Boolean);
    var space = isMask ? PsColourSpace.Gray : interpreter.Graphics.Space;
    var components = isMask ? 1 : space switch { PsColourSpace.Gray => 1, PsColourSpace.Rgb => 3, _ => 4 };
    var bits = isMask ? 1 : _Integer(dictionary, "BitsPerComponent", 8);

    var decode = new double[components * 2];
    for (var index = 0; index < components; ++index) {
      decode[index * 2] = 0;
      decode[index * 2 + 1] = 1;
    }

    if (dictionary.TryGet("Decode", out var decodeValue)) {
      if (decodeValue.Type != PsType.Array || decodeValue.Array.Length != decode.Length)
        throw new PsErrorException("rangecheck", $"A PostScript image states a Decode of {(decodeValue.Type == PsType.Array ? decodeValue.Array.Length : 0)} numbers where {decode.Length} belong.");

      for (var index = 0; index < decode.Length; ++index)
        decode[index] = decodeValue.Array[index].Number;
    }

    var raster = new Raster {
      Width = width,
      Height = height,
      Bits = bits,
      Components = components,
      Space = space,
      Decode = decode,
      ImageMatrix = PostScriptGraphicsOperators.ToMatrix(matrixValue.Array),
      IsMask = isMask
    };

    _Check(raster);

    if (dictionary.TryGet("MultipleDataSources", out var multiple) && multiple.Type == PsType.Boolean && multiple.Boolean)
      throw new PsUnsupportedException("A PostScript image whose components come from separate sources, which this reader does not interleave.");

    if (!dictionary.TryGet("DataSource", out var source))
      throw new PsErrorException("rangecheck", "A PostScript image dictionary with nothing to read its samples from.");

    raster.Data = _Read(interpreter, source, (long)raster.RowBytes * raster.Height);
    return raster;
  }

  /// <summary>
  /// A colour image, whose components may be interleaved in one source or split across several.
  /// </summary>
  /// <remarks>
  /// <c>width height bits matrix source... multi ncomp colorimage</c>. One component is grey, three
  /// are red, green and blue, and four are the subtractive set; the reference allows no other count.
  /// </remarks>
  private static void _ColourImage(PostScriptInterpreter interpreter) {
    var components = (int)interpreter.PopInteger();
    var multiple = interpreter.PopBoolean();
    if (components is not (1 or 3 or 4))
      throw new PsErrorException("rangecheck", $"A colorimage of {components} components, which the language does not define.");

    var sources = new PsObject[multiple ? components : 1];
    for (var index = sources.Length - 1; index >= 0; --index)
      sources[index] = interpreter.Pop();

    var matrix = PostScriptGraphicsOperators.ToMatrix(interpreter.PopArray());
    var bits = (int)interpreter.PopInteger();
    var height = (int)interpreter.PopInteger();
    var width = (int)interpreter.PopInteger();

    var decode = new double[components * 2];
    for (var index = 0; index < components; ++index)
      decode[index * 2 + 1] = 1;

    var raster = new Raster {
      Width = width,
      Height = height,
      Bits = bits,
      Components = components,
      Space = components switch { 1 => PsColourSpace.Gray, 3 => PsColourSpace.Rgb, _ => PsColourSpace.Cmyk },
      Decode = decode,
      ImageMatrix = matrix,
      IsMask = false
    };

    _Check(raster);

    if (!multiple) {
      raster.Data = _Read(interpreter, sources[0], (long)raster.RowBytes * raster.Height);
      _Draw(interpreter, raster);
      return;
    }

    // Separate sources hand over one component at a time, a whole plane each. Interleaving them
    // here rather than teaching the sampler about planes keeps one layout downstream.
    var planeRow = (int)(((long)width * bits + 7) / 8);
    var planes = new byte[components][];
    for (var index = 0; index < components; ++index)
      planes[index] = _Read(interpreter, sources[index], (long)planeRow * height);

    var data = new byte[(long)raster.RowBytes * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
    for (var component = 0; component < components; ++component) {
      var value = ReadBits(planes[component], (long)y * planeRow * 8 + (long)x * bits, bits);
      _PutBits(data, (long)y * raster.RowBytes * 8 + ((long)x * components + component) * bits, bits, value);
    }

    raster.Data = data;
    _Draw(interpreter, raster);
  }

  private static void _Check(Raster raster) {
    if (raster.Width <= 0 || raster.Height <= 0)
      throw new PsErrorException("rangecheck", $"A PostScript image of {raster.Width} by {raster.Height} samples.");

    if (raster.Bits is not (1 or 2 or 4 or 8 or 16))
      throw new PsErrorException("rangecheck", $"A PostScript image of {raster.Bits} bits a component, which the language does not define.");

    if ((long)raster.Width * raster.Height * raster.Components > MaximumSamples)
      throw new PsErrorException("limitcheck", $"A PostScript image of {raster.Width} by {raster.Height} samples is larger than this will draw.");
  }

  /// <summary>
  /// Collects the bytes of an image from wherever the program says they are.
  /// </summary>
  /// <remarks>
  /// A source is a string, a file, or a procedure that hands back a piece at a time — the last being
  /// how a program writes its image data into its own text and reads it back with
  /// <c>readhexstring</c>. A procedure that hands back nothing is the end of the data, and an image
  /// that runs out early is refused rather than drawn with whatever the rest of memory held.
  /// </remarks>
  private static byte[] _Read(PostScriptInterpreter interpreter, PsObject source, long wanted) {
    if (wanted > MaximumSamples)
      throw new PsErrorException("limitcheck", $"A PostScript image of {wanted} bytes is larger than this will draw.");

    var data = new byte[wanted];
    var have = 0;

    switch (source.Type) {
      case PsType.String:
        have = Math.Min((int)wanted, source.String.Length);
        source.String.Span[..have].CopyTo(data);
        break;

      case PsType.File: {
        var file = source.File;
        while (have < wanted) {
          var value = file.ReadByte();
          if (value < 0)
            break;

          data[have++] = (byte)value;
        }

        break;
      }

      case PsType.Array when source.IsExecutable: {
        while (have < wanted) {
          var before = interpreter.Count;
          interpreter.RunNested(source);
          if (interpreter.Count <= before)
            throw new PsErrorException("typecheck", "A PostScript image data procedure returned nothing.");

          var piece = interpreter.Pop();
          if (piece.Type != PsType.String)
            throw new PsErrorException("typecheck", $"A PostScript image data procedure returned {piece.TypeName}.");

          if (piece.String.Length == 0)
            break;

          var take = (int)Math.Min(piece.String.Length, wanted - have);
          piece.String.Span[..take].CopyTo(data.AsSpan(have));
          have += take;
        }

        break;
      }

      default:
        throw new PsErrorException("typecheck", $"A PostScript image was told to read its samples from {source.TypeName}.");
    }

    if (have < wanted)
      throw new InvalidDataException($"A PostScript image states {wanted} bytes of samples and the file carries {have}.");

    return data;
  }

  /// <summary>Puts the raster on the page, as a fill of the square it occupies.</summary>
  private static void _Draw(PostScriptInterpreter interpreter, Raster raster) {
    var state = interpreter.Graphics;

    // The image fills the unit square of user space; the matrix says how the samples are arranged
    // inside it. So a point on the page goes back through the transform to user space and on
    // through the image matrix to a row and a column.
    var toImage = PsMatrix.Inverse(state.Ctm).Then(raster.ImageMatrix);

    var square = new VectorPath();
    var (x0, y0) = state.Ctm.Apply(0, 0);
    var (x1, y1) = state.Ctm.Apply(1, 0);
    var (x2, y2) = state.Ctm.Apply(1, 1);
    var (x3, y3) = state.Ctm.Apply(0, 1);
    square.MoveTo(x0, y0);
    square.LineTo(x1, y1);
    square.LineTo(x2, y2);
    square.LineTo(x3, y3);
    square.Close();

    var paint = new PsImagePaint(raster.Data, raster.Width, raster.Height, raster.Bits, raster.Components, raster.RowBytes, raster.Space, raster.Decode, toImage, raster.IsMask ? state.Colour : null);
    interpreter.Page.Fill(state, square, FillRule.NonZero, paint);
  }

  private static int _Integer(PsDictionary dictionary, string key, int fallback)
    => dictionary.TryGet(key, out var value) && value.IsNumber ? (int)value.Number : fallback;

  /// <summary>Reads a run of bits out of a byte array, most significant first.</summary>
  internal static int ReadBits(byte[] data, long bitOffset, int bits) {
    var value = 0;
    for (var index = 0; index < bits; ++index) {
      var at = bitOffset + index;
      var byteAt = at >> 3;
      var bit = byteAt < data.Length ? (data[byteAt] >> (7 - (int)(at & 7))) & 1 : 0;
      value = (value << 1) | bit;
    }

    return value;
  }

  private static void _PutBits(byte[] data, long bitOffset, int bits, int value) {
    for (var index = 0; index < bits; ++index) {
      var at = bitOffset + index;
      var byteAt = at >> 3;
      if (byteAt >= data.Length)
        return;

      var bit = (value >> (bits - 1 - index)) & 1;
      var shift = 7 - (int)(at & 7);
      data[byteAt] = (byte)((data[byteAt] & ~(1 << shift)) | (bit << shift));
    }
  }
}

/// <summary>The colour an image has at a place on the page.</summary>
/// <remarks>
/// Nearest sample rather than an interpolated one. An image drawn larger than its samples is a grid
/// of squares, which is what the samples say; smoothing them would invent detail the file does not
/// carry, and an image drawn smaller loses samples either way.
/// </remarks>
internal sealed class PsImagePaint(
  byte[] data,
  int width,
  int height,
  int bits,
  int components,
  int rowBytes,
  PsColourSpace space,
  double[] decode,
  Matrix2D toImage,
  Rgba32? maskColour
) : VectorPaint {

  private readonly double _maximum = (1 << bits) - 1;
  private readonly double[] _values = new double[components];

  public override Rgba32 At(double x, double y) {
    var (u, v) = toImage.Apply(x, y);
    var column = (int)Math.Floor(u);
    var row = (int)Math.Floor(v);

    // A pixel whose centre falls just outside the square, because the edge runs diagonally through
    // it, takes the nearest sample rather than nothing: the coverage already says how much of it is
    // inside.
    column = Math.Clamp(column, 0, width - 1);
    row = Math.Clamp(row, 0, height - 1);

    var bitOffset = (long)row * rowBytes * 8 + (long)column * components * bits;
    for (var component = 0; component < components; ++component) {
      var raw = PostScriptImages.ReadBits(data, bitOffset + (long)component * bits, bits);
      var low = decode[component * 2];
      var high = decode[component * 2 + 1];
      this._values[component] = low + raw * (high - low) / this._maximum;
    }

    // A mask paints the current colour where its samples say to and leaves the page alone elsewhere,
    // which is transparent rather than white.
    if (maskColour != null)
      return this._values[0] < 0.5 ? maskColour.Value : new(0, 0, 0, 0);

    return PsColour.From(space, this._values);
  }
}
