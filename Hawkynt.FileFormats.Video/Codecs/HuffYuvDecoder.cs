using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.HuffYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes HuffYUV and its extension FFVHUFF: Huffman coding over the difference between each sample
/// and a prediction of it from the samples already decoded.
/// </summary>
/// <remarks>
/// Lossless, and intra only — every frame stands alone, which is what makes it an editing codec.
/// There is no transform and no quantiser anywhere in it. A sample is predicted from its neighbours,
/// the difference is coded with one Huffman table per plane, and the tables are in the stream
/// description rather than in the frames unless the writer says otherwise.
/// <para/>
/// <b>The bits are in little-endian words.</b> The coder wrote whole machine words on a little-endian
/// machine, so every four bytes of a frame sit back to front and have to be swapped before a bit of
/// it means anything. The first pixel of a frame is stored raw and comes out in reverse — alpha,
/// red, green, blue — which is that swap showing through. See <see cref="HuffYuvBitReader"/>.
/// <para/>
/// <b>The codes are handed out from the longest length down</b>, not the shortest up, so the
/// canonical assignment a reader would reach for decodes nothing. See
/// <see cref="HuffYuvHuffmanTable"/>.
/// <para/>
/// <b>Three header forms and three frame layouts.</b> A description whose fourth byte is one codes
/// its planes one after another and states its subsampling; one whose fourth byte is zero codes
/// 4:2:2 groups interleaved along each row, or colour a pixel at a time bottom row first. Which of
/// the two a file uses is not the four-character code — <c>HFYU</c> and <c>FFVH</c> both write both —
/// so it is read off the description. See <see cref="HuffYuvFormat"/>.
/// <para/>
/// <b>Measured against ffmpeg.</b> Every pixel format its two encoders will write was encoded with
/// each of the three predictors and decoded here and by ffmpeg. The formats that need no colour
/// conversion — <c>gray</c>, <c>gbrp</c>, <c>gbrap</c>, <c>rgb24</c>, <c>bgra</c> — are compared
/// against ffmpeg's own frames directly and are identical, pixel for pixel, on every frame. The
/// planes of the luminance-and-chrominance formats are compared against ffmpeg's decoded planes
/// through the same conversion, and are identical as well.
/// <para/>
/// <b>What refuses.</b> The original codec, whose frames carry no description at all; samples deeper
/// than eight bits; a description that states neither interlaced nor progressive and expects the
/// height to be guessed from; a prediction method that is none of the three; a Huffman table whose
/// lengths do not describe a complete code. There is no <c>catch</c> here returning a blank or a
/// repeated frame.
/// </remarks>
public sealed class HuffYuvDecoder : IVideoCodecDecoder<HuffYuvDecoder> {

  /// <summary>The codes the two spellings of this codec are named by.</summary>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("HFYU"),
    CodecTag.FromCharacters("FFVH"),
  ];

  private readonly int _width;
  private readonly int _height;
  private readonly HuffYuvFormat _format;
  private readonly HuffYuvHuffmanTable[]? _tables;

  private HuffYuvDecoder(int width, int height, HuffYuvFormat format, HuffYuvHuffmanTable[]? tables) {
    this._width = width;
    this._height = height;
    this._format = format;
    this._tables = tables;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "HuffYUV / FFVHUFF";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder from the stream description, which for this codec is a
  /// <c>BITMAPINFOHEADER</c> with the codec's own four bytes and its Huffman tables behind it.
  /// </summary>
  /// <remarks>
  /// The tables are read here and not per frame, because that is where they are: a stream states
  /// them once and every frame uses them. A writer that puts them in each frame instead says so in
  /// the description, and then this reads none and each frame brings its own.
  /// </remarks>
  public static HuffYuvDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    var description = stream.CodecPrivateData.Span;
    var extra = description.Length > BitmapInfoHeader.StructSize ? description[BitmapInfoHeader.StructSize..] : default;

    HuffYuvFormat.RefuseUnstatedInterlacing(extra, stream.Height, stream.Index);
    var format = HuffYuvFormat.Parse(extra, stream.BitsPerPixel, stream.Index);

    _RefuseInterlacedHalfHeightMedian(format, stream.Index);

    var tables = format.TablesPerFrame
      ? null
      : HuffYuvHuffmanTable.ReadAll(extra, 4, format.TableCount, out _);

    return new(stream.Width, stream.Height, format, tables);
  }

  /// <summary>
  /// Refuses the one arrangement of the interleaved layout whose row order could not be established.
  /// </summary>
  /// <remarks>
  /// Interlaced 4:2:0 with median prediction, and only that. The interleaved layout writes a 4:2:0
  /// frame as rows that carry chrominance alternating with rows that do not, and median prediction
  /// already shifts that alternation by one row; a frame of two fields shifts it again, and the
  /// resulting order does not follow from either shift on its own. Reading it as the nearest thing
  /// that does work reproduces the first five rows of such a frame and then diverges, which is
  /// exactly the plausible-but-wrong picture this decoder must not hand back. Every other
  /// combination of the three — interlaced 4:2:0 predicted from the left or by gradient, interlaced
  /// 4:2:2 with median, progressive 4:2:0 with median, and every planar arrangement including the
  /// interlaced median one — is decoded and measured.
  /// </remarks>
  private static void _RefuseInterlacedHalfHeightMedian(HuffYuvFormat format, int streamIndex) {
    if (!format.Interlaced || format.Predictor != HuffYuvPredictor.Median || format.BitstreamBitsPerPixel != 12)
      return;

    throw new NotSupportedException(
      $"Video stream {streamIndex} is interlaced 4:2:0 coded as interleaved rows with median prediction. The order its rows are written in could not be established against any file, and reading it as the nearest arrangement that is known reproduces five rows of a frame and then diverges — so it is refused rather than half decoded. Interlaced 4:2:0 predicted from the left or by gradient, interlaced 4:2:2 with median, and the planar form of all three are read.");
  }

  /// <summary>Decodes one frame, which for this codec is always exactly one whole picture.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var bits = new HuffYuvBitReader(packet.Data.Span);

    var tables = this._tables;
    if (tables == null) {
      tables = HuffYuvHuffmanTable.ReadAll(bits.Swapped, 0, this._format.TableCount, out var start);
      bits.SeekToByte(start);
    }

    frame = this._format.ColourSpace == HuffYuvColourSpace.PackedBgr
      ? this._DecodePackedColour(bits, tables)
      : this._DecodePlanes(bits, tables);

    return true;
  }

  // ============================================================================================
  // The planar shapes
  // ============================================================================================

  private RawImage _DecodePlanes(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables) {
    var planes = this._format.Version >= 3
      ? this._DecodePlaneAtATime(bits, tables)
      : this._DecodeInterleavedRows(bits, tables);

    return this._Compose(planes);
  }

  /// <summary>
  /// The third form: every plane decoded through to its last row before the next one begins.
  /// </summary>
  private HuffYuvPlane[] _DecodePlaneAtATime(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables) {
    var planes = this._AllocatePlanes();
    var differences = new byte[this._width];
    var above = this._format.Interlaced ? 2 : 1;

    for (var index = 0; index < planes.Length; ++index) {
      var plane = planes[index];
      var table = tables[index];
      var width = plane.Width;
      byte left = 0;
      byte leftAbove = 0;

      for (var y = 0; y < plane.Height; ++y) {
        this._ReadSymbols(bits, table, differences, width);

        // The first row or two have nothing above them, so they are read as differences from the
        // left whatever the predictor is. Median prediction then starts from the row after.
        if (y < above || this._format.Predictor != HuffYuvPredictor.Median) {
          left = HuffYuvPrediction.AddLeft(plane.Row(y), differences, width, left);
          if (this._format.Predictor == HuffYuvPredictor.Gradient && y >= above)
            HuffYuvPrediction.AddAbove(plane.Row(y), plane.ReadRow(y - above), width);

          if (y == above - 1)
            leftAbove = plane.Samples[0];

          continue;
        }

        HuffYuvPrediction.AddMedian(plane.Row(y), plane.ReadRow(y - above), differences, width, ref left, ref leftAbove);
      }
    }

    return planes;
  }

  /// <summary>
  /// The second form's luminance-and-chrominance layout: 4:2:2 groups along a row.
  /// </summary>
  /// <remarks>
  /// A row is written as <c>Y U Y V</c> repeated, so both chrominance planes advance once per two
  /// luminance samples. A 4:2:0 stream is the same thing with the rows that carry no chrominance
  /// written as luminance alone, so the chrominance row index advances once per pair of luminance
  /// rows and has to be tracked apart from the luminance one.
  /// <para/>
  /// The first four samples of a frame are raw rather than coded, and they arrive in the order the
  /// word swap leaves them: the second chrominance sample, the second luminance sample, the first
  /// chrominance sample, the first luminance sample.
  /// <para/>
  /// <b>Median prediction shifts the rows by one.</b> Where left and gradient prediction write a
  /// 4:2:0 stream as luminance row, then a row of all three, over and over, median prediction makes
  /// the row after the first a row of all three and only then falls into that alternation — so the
  /// second and third rows of the picture both carry luminance alone. It is not a decoration: it is
  /// what makes the chrominance rows come out at twenty-four for a forty-eight row picture instead of
  /// twenty-five. Reading it the other way puts the whole frame one Huffman code out of step from the
  /// eighteenth sample of the second row onwards, which was how it was found.
  /// </remarks>
  private HuffYuvPlane[] _DecodeInterleavedRows(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables) {
    var planes = this._AllocatePlanes();
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var chromaWidth = cb.Width;
    var lumaDifferences = new byte[this._width];
    var cbDifferences = new byte[chromaWidth];
    var crDifferences = new byte[chromaWidth];
    var halfHeight = this._format.BitstreamBitsPerPixel == 12;

    if (this._width < 2 || chromaWidth < 1)
      throw new InvalidDataException(
        $"A {this._width}-pixel wide picture cannot be coded as 4:2:2 groups, which are two pixels each.");

    var leftV = (byte)bits.Bits(8);
    var lumaOne = (byte)bits.Bits(8);
    var leftU = (byte)bits.Bits(8);
    var lumaZero = (byte)bits.Bits(8);

    luma.Samples[0] = lumaZero;
    luma.Samples[1] = lumaOne;
    cb.Samples[0] = leftU;
    cr.Samples[0] = leftV;

    var leftY = lumaOne;

    this._Read422(bits, tables, lumaDifferences, cbDifferences, crDifferences, this._width - 2);
    leftY = HuffYuvPrediction.AddLeft(luma.Row(0)[2..], lumaDifferences, this._width - 2, leftY);
    leftU = HuffYuvPrediction.AddLeft(cb.Row(0)[1..], cbDifferences, chromaWidth - 1, leftU);
    leftV = HuffYuvPrediction.AddLeft(cr.Row(0)[1..], crDifferences, chromaWidth - 1, leftV);

    var leftAboveY = lumaZero;
    var leftAboveU = cb.Samples[0];
    var leftAboveV = cr.Samples[0];

    var y = 1;
    var chromaRow = 1;

    if (this._format.Predictor == HuffYuvPredictor.Median) {
      var above = this._format.Interlaced ? 2 : 1;

      if (this._format.Interlaced) {
        // A frame of two fields has two rows with nothing above them, so the second is read from the
        // left as the first was.
        this._Read422(bits, tables, lumaDifferences, cbDifferences, crDifferences, this._width);
        leftY = HuffYuvPrediction.AddLeft(luma.Row(1), lumaDifferences, this._width, leftY);
        leftU = HuffYuvPrediction.AddLeft(cb.Row(1), cbDifferences, chromaWidth, leftU);
        leftV = HuffYuvPrediction.AddLeft(cr.Row(1), crDifferences, chromaWidth, leftV);
        y = 2;
        chromaRow = 2;
      }

      // The first row that has a row above it carries all three planes whatever the subsampling —
      // and its first four luminance samples, with the two chrominance samples beside them, are read
      // from the left rather than from the median. That last part is easy to miss: a picture whose
      // second row repeats its first hides it completely, which is why it only showed up on content
      // busy enough for the two to disagree.
      var lumaLeft = Math.Min(4, this._width);
      var chromaLeft = Math.Min(2, chromaWidth);

      this._Read422(bits, tables, lumaDifferences, cbDifferences, crDifferences, this._width);
      leftY = HuffYuvPrediction.AddLeft(luma.Row(y), lumaDifferences, lumaLeft, leftY);
      leftU = HuffYuvPrediction.AddLeft(cb.Row(chromaRow), cbDifferences, chromaLeft, leftU);
      leftV = HuffYuvPrediction.AddLeft(cr.Row(chromaRow), crDifferences, chromaLeft, leftV);

      leftAboveY = luma.ReadRow(y - above)[lumaLeft - 1];
      leftAboveU = cb.ReadRow(chromaRow - above)[chromaLeft - 1];
      leftAboveV = cr.ReadRow(chromaRow - above)[chromaLeft - 1];

      HuffYuvPrediction.AddMedian(
        luma.Row(y)[lumaLeft..], luma.ReadRow(y - above)[lumaLeft..], lumaDifferences.AsSpan(lumaLeft),
        this._width - lumaLeft, ref leftY, ref leftAboveY);
      HuffYuvPrediction.AddMedian(
        cb.Row(chromaRow)[chromaLeft..], cb.ReadRow(chromaRow - above)[chromaLeft..], cbDifferences.AsSpan(chromaLeft),
        chromaWidth - chromaLeft, ref leftU, ref leftAboveU);
      HuffYuvPrediction.AddMedian(
        cr.Row(chromaRow)[chromaLeft..], cr.ReadRow(chromaRow - above)[chromaLeft..], crDifferences.AsSpan(chromaLeft),
        chromaWidth - chromaLeft, ref leftV, ref leftAboveV);

      ++y;
      ++chromaRow;

      if (halfHeight && y < this._height) {
        this._ReadSymbols(bits, tables[0], lumaDifferences, this._width);
        HuffYuvPrediction.AddMedian(luma.Row(y), luma.ReadRow(y - above), lumaDifferences, this._width, ref leftY, ref leftAboveY);
        ++y;
      }
    }

    for (; y < this._height; ++y, ++chromaRow) {
      if (halfHeight) {
        this._ReadSymbols(bits, tables[0], lumaDifferences, this._width);
        this._ApplyRow(luma, y, lumaDifferences, this._width, ref leftY, ref leftAboveY);

        ++y;
        if (y >= this._height)
          break;
      }

      this._Read422(bits, tables, lumaDifferences, cbDifferences, crDifferences, this._width);
      this._ApplyRow(luma, y, lumaDifferences, this._width, ref leftY, ref leftAboveY);

      if (chromaRow >= cb.Height)
        continue;

      this._ApplyRow(cb, chromaRow, cbDifferences, chromaWidth, ref leftU, ref leftAboveU);
      this._ApplyRow(cr, chromaRow, crDifferences, chromaWidth, ref leftV, ref leftAboveV);
    }

    bits.RefuseIfExhausted("a luminance and chrominance frame");
    return planes;
  }

  /// <summary>Turns one row of differences into samples, whichever predictor the stream names.</summary>
  private void _ApplyRow(
    HuffYuvPlane plane, int y, ReadOnlySpan<byte> differences, int count, ref byte left, ref byte leftAbove) {
    var above = this._format.Interlaced ? 2 : 1;

    if (this._format.Predictor == HuffYuvPredictor.Median && y >= above) {
      HuffYuvPrediction.AddMedian(plane.Row(y), plane.ReadRow(y - above), differences, count, ref left, ref leftAbove);
      return;
    }

    left = HuffYuvPrediction.AddLeft(plane.Row(y), differences, count, left);
    if (this._format.Predictor == HuffYuvPredictor.Gradient && y >= above)
      HuffYuvPrediction.AddAbove(plane.Row(y), plane.ReadRow(y - above), count);
  }

  private void _Read422(
    HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables, Span<byte> luma, Span<byte> cb, Span<byte> cr, int count) {
    var groups = count / 2;
    for (var i = 0; i < groups; ++i) {
      luma[2 * i] = (byte)tables[0].Read(bits);
      cb[i] = (byte)tables[1].Read(bits);
      luma[2 * i + 1] = (byte)tables[0].Read(bits);
      cr[i] = (byte)tables[2].Read(bits);
    }
  }

  private void _ReadSymbols(HuffYuvBitReader bits, HuffYuvHuffmanTable table, Span<byte> into, int count) {
    for (var i = 0; i < count; ++i)
      into[i] = (byte)table.Read(bits);
  }

  private HuffYuvPlane[] _AllocatePlanes() {
    switch (this._format.ColourSpace) {
      case HuffYuvColourSpace.Grey:
        return [new(this._width, this._height)];
      case HuffYuvColourSpace.PlanarRgb: {
        var rgb = new HuffYuvPlane[this._format.HasAlpha ? 4 : 3];
        for (var i = 0; i < rgb.Length; ++i)
          rgb[i] = new(this._width, this._height);

        return rgb;
      }
      default: {
        var chromaWidth = this._width >> this._format.ChromaHorizontalShift;
        var chromaHeight = this._height >> this._format.ChromaVerticalShift;
        var yuv = new HuffYuvPlane[this._format.HasAlpha ? 4 : 3];
        yuv[0] = new(this._width, this._height);
        yuv[1] = new(chromaWidth, chromaHeight);
        yuv[2] = new(chromaWidth, chromaHeight);
        if (yuv.Length == 4)
          yuv[3] = new(this._width, this._height);

        return yuv;
      }
    }
  }

  // ============================================================================================
  // The packed shape
  // ============================================================================================

  /// <summary>
  /// The second form's colour layout: four bytes a pixel, blue first, bottom row first.
  /// </summary>
  /// <remarks>
  /// Bottom row first because that is how a Windows bitmap is stored and this codec was written to
  /// be one. The rows above are therefore predicted from the row <i>after</i> them in the buffer, and
  /// the first pixel of the frame is the bottom-left one.
  /// <para/>
  /// A twenty-four bit stream still fills four bytes a pixel, with the fourth left opaque. That is
  /// not padding this decoder invents: ffmpeg reports such a stream as <c>bgr0</c> for the same
  /// reason, and the alpha of a stream that has one is coded like any other channel.
  /// </remarks>
  private RawImage _DecodePackedColour(HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables) {
    const int _B = 0;
    const int _G = 1;
    const int _R = 2;
    const int _A = 3;

    var hasAlpha = this._format.BitstreamBitsPerPixel == 32;
    var pixels = new byte[this._width * this._height * 4];
    var stride = this._width * 4;
    var differences = new byte[this._width * 4];
    var above = this._format.Interlaced ? 2 : 1;

    // The first pixel is raw, and it takes a whole word whether or not the stream has an alpha
    // channel to put in the fourth byte of it. Where there is one it comes first, ahead of red,
    // green and blue; where there is not, the spare byte is at the other end and is read past.
    // Which end it sits at was measured on a frame of flat red, whose first word after the swap is
    // 253, 0, 0, 0 for a picture ffmpeg decodes as red 253 — so red is the first of the four and the
    // spare byte the last.
    var bottom = (this._height - 1) * stride;
    if (hasAlpha)
      pixels[bottom + _A] = (byte)bits.Bits(8);

    var red = (byte)bits.Bits(8);
    var green = (byte)bits.Bits(8);
    var blue = (byte)bits.Bits(8);
    if (!hasAlpha)
      bits.Bits(8);

    // The raw pixel is the colour itself and not its distance from green, where every coded
    // difference after it is. It is put into the decorrelated form the rest of the frame is in, so
    // that one pass over the picture at the end brings all of it back together. Measured on a frame
    // whose first pixel is 0, 61, 103: reading it as already decorrelated makes it 61, 61, 164.
    if (this._format.Decorrelate) {
      red -= green;
      blue -= green;
    }

    pixels[bottom + _R] = red;
    pixels[bottom + _G] = green;
    pixels[bottom + _B] = blue;

    var left = new byte[4];
    left[_B] = pixels[bottom + _B];
    left[_G] = pixels[bottom + _G];
    left[_R] = pixels[bottom + _R];
    left[_A] = pixels[bottom + _A];

    this._ReadColour(bits, tables, differences, this._width - 1, hasAlpha);
    _AddLeftColour(pixels.AsSpan(bottom + 4), differences, this._width - 1, left);

    for (var y = this._height - 2; y >= 0; --y) {
      this._ReadColour(bits, tables, differences, this._width, hasAlpha);
      var row = pixels.AsSpan(y * stride, stride);
      _AddLeftColour(row, differences, this._width, left);

      if (this._format.Predictor == HuffYuvPredictor.Gradient && y <= this._height - 1 - above)
        HuffYuvPrediction.AddAbove(row, pixels.AsSpan((y + above) * stride, stride), stride);
    }

    if (this._format.Decorrelate)
      _Correlate(pixels);

    if (!hasAlpha)
      for (var i = _A; i < pixels.Length; i += 4)
        pixels[i] = 255;

    return this._ToImage(pixels, hasAlpha);
  }

  /// <summary>Reads one row's worth of colour differences, in the order the frame carries them.</summary>
  private void _ReadColour(
    HuffYuvBitReader bits, HuffYuvHuffmanTable[] tables, Span<byte> into, int count, bool hasAlpha) {
    for (var i = 0; i < count; ++i) {
      var at = i * 4;
      into[at + 1] = (byte)tables[1].Read(bits);
      into[at + 0] = (byte)tables[0].Read(bits);
      into[at + 2] = (byte)tables[2].Read(bits);
      into[at + 3] = hasAlpha ? (byte)tables[2].Read(bits) : (byte)0;
    }
  }

  private static void _AddLeftColour(Span<byte> row, ReadOnlySpan<byte> differences, int count, byte[] left) {
    for (var i = 0; i < count; ++i) {
      var at = i * 4;
      row[at] = left[0] = (byte)(left[0] + differences[at]);
      row[at + 1] = left[1] = (byte)(left[1] + differences[at + 1]);
      row[at + 2] = left[2] = (byte)(left[2] + differences[at + 2]);
      row[at + 3] = left[3] = (byte)(left[3] + differences[at + 3]);
    }
  }

  /// <summary>
  /// Undoes the decorrelation: blue and red are stored as their distance from green.
  /// </summary>
  /// <remarks>
  /// A picture is mostly grey, so blue minus green and red minus green are small where blue and red
  /// are not, and small numbers are what a Huffman table is good at. Adding green back is the whole
  /// of the inverse, and it happens after the spatial prediction has been undone because both are
  /// additions and the order between them does not matter.
  /// </remarks>
  private static void _Correlate(Span<byte> pixels) {
    for (var i = 0; i < pixels.Length; i += 4) {
      var green = pixels[i + 1];
      pixels[i] += green;
      pixels[i + 2] += green;
    }
  }

  // ============================================================================================
  // What comes out
  // ============================================================================================

  private RawImage _ToImage(byte[] bgra, bool hasAlpha) {
    if (hasAlpha) {
      var rgba = new byte[this._width * this._height * 4];
      for (var i = 0; i < rgba.Length; i += 4) {
        rgba[i] = bgra[i + 2];
        rgba[i + 1] = bgra[i + 1];
        rgba[i + 2] = bgra[i];
        rgba[i + 3] = bgra[i + 3];
      }

      return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgba32, PixelData = rgba };
    }

    var rgb = new byte[this._width * this._height * 3];
    for (var i = 0; i < this._width * this._height; ++i) {
      rgb[i * 3] = bgra[i * 4 + 2];
      rgb[i * 3 + 1] = bgra[i * 4 + 1];
      rgb[i * 3 + 2] = bgra[i * 4];
    }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private RawImage _Compose(HuffYuvPlane[] planes) => this._format.ColourSpace switch {
    HuffYuvColourSpace.Grey => new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Gray8,
      PixelData = planes[0].Samples,
    },
    HuffYuvColourSpace.PlanarRgb => this._FromPlanarRgb(planes),
    _ => this._FromYuv(planes),
  };

  /// <summary>
  /// Puts the three colour planes back in order. A planar colour stream stores them green, blue,
  /// red, which is the order the format's own name for it — <c>GBR</c> — states.
  /// </summary>
  private RawImage _FromPlanarRgb(HuffYuvPlane[] planes) {
    var count = this._width * this._height;
    var green = planes[0].Samples;
    var blue = planes[1].Samples;
    var red = planes[2].Samples;

    if (this._format.HasAlpha) {
      var rgba = new byte[count * 4];
      var alpha = planes[3].Samples;
      for (var i = 0; i < count; ++i) {
        rgba[i * 4] = red[i];
        rgba[i * 4 + 1] = green[i];
        rgba[i * 4 + 2] = blue[i];
        rgba[i * 4 + 3] = alpha[i];
      }

      return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgba32, PixelData = rgba };
    }

    var rgb = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      rgb[i * 3] = red[i];
      rgb[i * 3 + 1] = green[i];
      rgb[i * 3 + 2] = blue[i];
    }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// Turns the luminance and chrominance planes into the packed colour every reader here hands back.
  /// </summary>
  /// <remarks>
  /// The conversion is a display convention and not part of the coding: HuffYUV codes samples and
  /// says nothing about what to do with them. The one used here is ITU-R BT.601 with studio swing —
  /// luminance running 16 to 235 rather than filling the byte — and each chrominance sample repeated
  /// across the block it covers, which is what a subsampled picture's samples mean and what the
  /// reference decoder's own conversion does.
  /// </remarks>
  private RawImage _FromYuv(HuffYuvPlane[] planes) {
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var alpha = this._format.HasAlpha ? planes[3] : null;
    var channels = alpha == null ? 3 : 4;
    var pixels = new byte[this._width * this._height * channels];

    for (var y = 0; y < this._height; ++y) {
      var chromaRow = Math.Min(y >> this._format.ChromaVerticalShift, cb.Height - 1);
      var lumaRow = y * luma.Width;
      var target = y * this._width * channels;

      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = Math.Min(x >> this._format.ChromaHorizontalShift, cb.Width - 1);
        var at = chromaRow * cb.Width + chromaColumn;

        var scaledLuma = 298 * (luma.Samples[lumaRow + x] - 16);
        var blueDifference = cb.Samples[at] - 128;
        var redDifference = cr.Samples[at] - 128;

        pixels[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        pixels[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        pixels[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        if (alpha != null)
          pixels[target + 3] = alpha.Samples[lumaRow + x];

        target += channels;
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = channels == 3 ? PixelFormat.Rgb24 : PixelFormat.Rgba32,
      PixelData = pixels,
    };
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
