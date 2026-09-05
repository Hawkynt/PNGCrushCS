using System;
using System.IO;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Everything Indeo 4 and Indeo 5 decode the same way: planes, wavelet bands, tiles, macroblocks,
/// blocks, the run-value coding of the coefficients, motion compensation and the wavelet
/// recomposition.
/// </summary>
/// <remarks>
/// The two formats are one codec with two picture headers. Below the header they agree on everything
/// — the same band and tile structure, the same run-value maps, the same codebook descriptors, the
/// same transforms, the same prediction — so the parts that differ are the three header readers and
/// the buffer rotation, which is what the abstract members here are and all that they are.
/// <para/>
/// <b>A picture is a tree, not a raster.</b> A plane holds one band or four; a band holds tiles; a
/// tile holds macroblocks; a macroblock holds one block or four. Only the innermost level carries
/// coefficients, and every level above it can say "nothing here" and mean something different by it:
/// an empty tile means the reference rectangle, possibly moved; an uncoded macroblock means the
/// reference block; an uncoded block means the reference block or, in an intra picture, a flat block
/// at the running DC value. None of those is a fallback for a read that failed — each is a coding
/// decision an encoder makes deliberately — which is why nothing here catches an error and produces
/// one of them.
/// <para/>
/// <b>Bands inherit from the first luminance band.</b> A chrominance band and a higher wavelet band
/// need not code their own macroblock types, quantisers or motion vectors: they can say that each of
/// their macroblocks takes them from the matching macroblock of the first luminance band, with motion
/// vectors scaled by the ratio of the two macroblock sizes. That is why the tiles of every band are
/// built together and why a band whose tiling does not match the first luminance band's is refused.
/// </remarks>
internal abstract class IviDecoder {

  /// <summary>The bit reader for the frame being decoded.</summary>
  protected IviBitReader Reader = new(ReadOnlyMemory<byte>.Empty);

  /// <summary>The three colour planes, luminance first.</summary>
  protected readonly IviPlane[] Planes = [new(), new(), new()];

  /// <summary>The picture layout in force, which a header changing means everything is rebuilt.</summary>
  protected IviPictureConfiguration PictureConfiguration;

  /// <summary>The nine run-value maps, as this decoder's own copies for bands to permute.</summary>
  private readonly IviRunValueMap[] _runValueMaps;

  /// <summary>The codebook macroblock signals are coded with.</summary>
  protected readonly IviCodebookSlot MacroblockCodebook = new();

  /// <summary>The picture-wide codebook block signals are coded with, which a band may override.</summary>
  protected readonly IviCodebookSlot BlockCodebook = new();

  protected int FrameType;

  protected int PreviousFrameType;

  protected bool IsScalable;

  /// <summary>Whether the group-of-pictures header was unreadable, which invalidates every frame in it.</summary>
  protected bool GroupInvalid;

  /// <summary>Whether the stream says it is scrambled, which is refused rather than guessed at.</summary>
  protected bool IsProtected;

  // The four band buffers rotate: which one is being decoded into, which one is predicted from, which
  // is the second reference of a bidirectional frame, and which serves the scalable mode.
  protected int DestinationBuffer;

  protected int ReferenceBuffer;

  protected int BackwardReferenceBuffer;

  protected int ScalableReferenceBuffer;

  private readonly bool[] _bufferInvalid = new bool[4];

  /// <summary>Scratch for one block's dequantised coefficients.</summary>
  private readonly int[] _coefficients = new int[64];

  /// <summary>Which columns of the block being transformed hold anything.</summary>
  private readonly byte[] _columnFlags = new byte[8];

  protected IviDecoder() {
    this._runValueMaps = new IviRunValueMap[IviRunValueMap.Defaults.Length];
    for (var i = 0; i < this._runValueMaps.Length; ++i)
      this._runValueMaps[i] = IviRunValueMap.Defaults[i].Clone();
  }

  /// <summary>Whether this is the Indeo 4 bitstream, which differs below the header in a few places.</summary>
  protected abstract bool IsIndeo4 { get; }

  /// <summary>Whether the frame just read carries picture data at all.</summary>
  protected abstract bool IsNonNullFrame { get; }

  /// <summary>
  /// Whether the frame just read predicts from both a past and a future picture, which only Indeo 4
  /// has and which is what makes the band buffers swap roles.
  /// </summary>
  protected virtual bool IsBidirectionalFrame => false;

  /// <summary>Reads the picture header, which is where the two formats part company.</summary>
  protected abstract void DecodePictureHeader();

  /// <summary>Reads one band's header.</summary>
  protected abstract void DecodeBandHeader(IviBand band);

  /// <summary>Reads the coding decisions of every macroblock of one tile.</summary>
  protected abstract void DecodeMacroblockInfo(IviBand band, IviTile tile);

  /// <summary>Rotates the band buffers for the frame about to be decoded.</summary>
  protected abstract void SwitchBuffers();

  // ============================================================================================
  // Frames
  // ============================================================================================

  /// <summary>
  /// Decodes one packet and returns the picture it produces, or nothing where the packet is a frame
  /// that shows nothing of its own.
  /// </summary>
  internal IviPicture? Decode(ReadOnlyMemory<byte> packet) {
    this.Reader = new(packet);
    this.DecodePictureHeader();

    if (this.GroupInvalid)
      throw new InvalidDataException(
        "This Indeo frame belongs to a group of pictures whose header could not be read, so the picture "
        + "layout every frame of the group depends on is unknown.");

    if (this.TakeDeferredPicture(out var deferred))
      return deferred;

    if (this.IsProtected)
      throw new NotSupportedException(
        "This Indeo clip is password-protected. Its frame data is scrambled with a key the file does not "
        + "carry, so there is nothing here that can be decoded without one.");

    if (this.Planes[0].Bands.Length == 0)
      throw new InvalidDataException(
        "This Indeo frame carries picture data before any header has stated how the picture is divided "
        + "into planes and bands.");

    this.SwitchBuffers();

    if (this.IsNonNullFrame) {
      this._bufferInvalid[this.DestinationBuffer] = true;

      foreach (var plane in this.Planes)
        foreach (var band in plane.Bands)
          this._DecodeBand(band);

      this._bufferInvalid[this.DestinationBuffer] = false;
    } else {
      if (this.IsScalable)
        throw new InvalidDataException(
          "This Indeo frame is an empty frame in a scalable stream, where the wavelet bands of the "
          + "previous frame are what an empty frame would have to repeat and no band holds them.");

      foreach (var plane in this.Planes)
        if (plane.Bands[0].Buffer.Length == 0)
          throw new InvalidDataException(
            "This Indeo stream begins with an empty frame, which states that nothing changed since a "
            + "frame that was never sent.");
    }

    if (this._bufferInvalid[this.DestinationBuffer])
      throw new InvalidDataException(
        "This Indeo frame predicts from a band buffer that an earlier frame left half-decoded.");

    if (!this.IsNonNullFrame)
      return null;

    var picture = this._Output();
    this.DeferTrailingPicture();

    return picture;
  }

  /// <summary>
  /// Hands back a picture an earlier packet decoded and held, where the frame just read is the one
  /// that asks for it. Indeo 4 alone does this.
  /// </summary>
  protected virtual bool TakeDeferredPicture(out IviPicture? picture) {
    picture = null;
    return false;
  }

  /// <summary>
  /// Decodes a second frame packed behind the one just decoded, to be handed back when a later frame
  /// asks for it. Indeo 4 alone does this.
  /// </summary>
  protected virtual void DeferTrailingPicture() { }

  private IviPicture _Output() {
    var luma = new byte[this.Planes[0].Width * this.Planes[0].Height];

    if (this.IsScalable)
      IviWavelet.Recompose(this.Planes[0], luma, this.Planes[0].Width, this.IsIndeo4);
    else
      _OutputPlane(this.Planes[0], luma);

    var chromaBlue = new byte[this.Planes[2].Width * this.Planes[2].Height];
    var chromaRed = new byte[this.Planes[1].Width * this.Planes[1].Height];
    _OutputPlane(this.Planes[2], chromaBlue);
    _OutputPlane(this.Planes[1], chromaRed);

    return new(
      this.Planes[0].Width, this.Planes[0].Height,
      this.Planes[2].Width, this.Planes[2].Height,
      luma, chromaBlue, chromaRed);
  }

  /// <summary>
  /// Turns one band's samples into bytes by adding back the bias the encoder took off.
  /// </summary>
  private static void _OutputPlane(IviPlane plane, byte[] destination) {
    var band = plane.Bands[0];
    var source = band.Buffer;
    if (source.Length == 0)
      return;

    for (var y = 0; y < plane.Height; ++y) {
      var from = y * band.Pitch;
      var to = y * plane.Width;

      for (var x = 0; x < plane.Width; ++x) {
        var value = source[from + x] + 128;
        destination[to + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  // ============================================================================================
  // Bands
  // ============================================================================================

  private void _DecodeBand(IviBand band) {
    band.Buffer = this._PrepareBuffer(band, this.DestinationBuffer)
      ?? throw new InvalidDataException(
        "This Indeo frame decodes into a band buffer the picture layout does not have. Only a scalable "
        + "picture has a third band buffer, and this stream is asking for one without being scalable.");

    if (this.IsBidirectionalFrame) {
      band.Reference = this._PrepareBuffer(band, this.BackwardReferenceBuffer);
      band.BackwardReference = this._PrepareBuffer(band, this.ReferenceBuffer);

      if (band.BackwardReference == null)
        throw new InvalidDataException(
          "This Indeo frame is bidirectional and predicts from a band buffer the picture layout does not have.");
    } else {
      band.Reference = this._PrepareBuffer(band, this.ReferenceBuffer);
      band.BackwardReference = null;
    }

    if (band.Reference == null)
      throw new InvalidDataException(
        "This Indeo frame predicts from a band buffer the picture layout does not have.");

    this.DecodeBandHeader(band);

    if (band.IsEmpty)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo frame says it holds nothing. A band "
        + "that carries no data has no meaning: it is the tiles within a band that may be empty, and a "
        + "picture needs every one of its bands.");

    band.RunValueMap = this._runValueMaps[band.RunValueMapSelector];

    for (var i = 0; i < band.CorrectionCount; ++i)
      band.RunValueMap.Swap(band.Corrections[i * 2], band.Corrections[i * 2 + 1]);

    try {
      this._DecodeTiles(band);
    } finally {
      // The map is shared with every other band that selects it, so the permutation has to come off
      // again whether the band decoded or not.
      for (var i = band.CorrectionCount - 1; i >= 0; --i)
        band.RunValueMap.Swap(band.Corrections[i * 2], band.Corrections[i * 2 + 1]);
    }

    this.Reader.Align();
  }

  private void _DecodeTiles(IviBand band) {
    var reference = this.Planes[0].Bands[0];
    var position = this.Reader.Position;

    foreach (var tile in band.Tiles) {
      if (tile.MacroblockSize != band.MacroblockSize || reference.MacroblockSize < band.MacroblockSize)
        throw new InvalidDataException(
          $"Band {band.BandNumber} of plane {band.Plane} of this Indeo frame states macroblocks of "
          + $"{band.MacroblockSize} samples where its tiles were built for {tile.MacroblockSize} and the "
          + $"first luminance band uses {reference.MacroblockSize}.");

      tile.IsEmpty = this.Reader.ReadFlag();

      if (tile.IsEmpty) {
        this._ProcessEmptyTile(band, tile, (reference.MacroblockSize >> 3) - (band.MacroblockSize >> 3));
        continue;
      }

      tile.DataSize = _ReadTileDataSize(this.Reader);
      if (tile.DataSize == 0)
        throw new InvalidDataException(
          $"A tile of band {band.BandNumber} of plane {band.Plane} of this Indeo frame states a size of "
          + "no bytes while also stating that it is not empty.");

      this.DecodeMacroblockInfo(band, tile);
      this._DecodeBlocks(band, tile);

      var read = (this.Reader.Position - position) >> 3;
      if (read != tile.DataSize)
        throw new InvalidDataException(
          $"A tile of band {band.BandNumber} of plane {band.Plane} of this Indeo frame states {tile.DataSize} "
          + $"bytes and its blocks accounted for {read}.");

      position += tile.DataSize << 3;
    }
  }

  /// <summary>
  /// Reads a tile's size, which is one byte where it fits in one and four where it does not.
  /// </summary>
  private static int _ReadTileDataSize(IviBitReader reader) {
    var length = 0;

    if (reader.ReadFlag()) {
      length = (int)reader.Read(8);
      if (length == 255)
        length = (int)reader.Read(24);
    }

    reader.Align();
    return length;
  }

  private short[]? _PrepareBuffer(IviBand band, int index) {
    if (this.PictureConfiguration.LumaBands <= 1 && index == 2)
      return null;

    return band.Buffers[index] ??= new short[band.BufferSize];
  }

  // ============================================================================================
  // Tiles, macroblocks and blocks
  // ============================================================================================

  /// <summary>
  /// Reconstructs a tile that carries no data: the same rectangle of the reference frame, moved by
  /// whatever motion vectors this band inherits.
  /// </summary>
  /// <param name="motionScale">
  /// How far to scale inherited motion vectors down, as the ratio of the first luminance band's
  /// macroblock size to this band's.
  /// </param>
  private void _ProcessEmptyTile(IviBand band, IviTile tile, int motionScale) {
    var macroblockSize = band.MacroblockSize;

    if (tile.Macroblocks.Length != _MacroblocksPerTile(tile.Width, tile.Height, macroblockSize))
      throw new InvalidDataException(
        $"An empty tile of band {band.BandNumber} of plane {band.Plane} of this Indeo frame is "
        + $"{tile.Width}x{tile.Height} at macroblocks of {macroblockSize}, which is not the "
        + $"{tile.Macroblocks.Length} macroblocks it was built with.");

    var reference = band.Reference!;
    var clearFirst = !band.QuantiserDeltaPresent && band.Plane == 0 && band.BandNumber == 0;
    var pitch = band.Pitch;
    var needsMotion = false;
    var index = 0;
    var offset = tile.Y * pitch + tile.X;

    for (var y = tile.Y; y < tile.Y + tile.Height; y += macroblockSize) {
      var macroblockOffset = offset;

      for (var x = tile.X; x < tile.X + tile.Width; x += macroblockSize) {
        var macroblock = tile.Macroblocks[index];
        macroblock.X = x;
        macroblock.Y = y;
        macroblock.BufferOffset = macroblockOffset;
        macroblock.Type = 1;
        macroblock.CodedBlockPattern = 0;

        if (clearFirst) {
          macroblock.QuantiserDelta = (sbyte)band.GlobalQuantiser;
          macroblock.MotionX = 0;
          macroblock.MotionY = 0;
        }

        if (tile.ReferenceMacroblocks != null) {
          var source = tile.ReferenceMacroblocks[index];

          if (band.InheritQuantiserDelta)
            macroblock.QuantiserDelta = source.QuantiserDelta;

          if (band.InheritMotionVectors) {
            macroblock.MotionX = ScaleMotion(source.MotionX, motionScale);
            macroblock.MotionY = ScaleMotion(source.MotionY, motionScale);
            needsMotion |= macroblock.MotionX != 0 || macroblock.MotionY != 0;
            _CheckMotionWithinBand(band, macroblock.X, macroblock.Y, macroblock.MotionX, macroblock.MotionY);
          }
        }

        ++index;
        macroblockOffset += macroblockSize;
      }

      offset += macroblockSize * pitch;
    }

    if (!band.InheritMotionVectors || !needsMotion) {
      for (var y = 0; y < tile.Height; ++y)
        Array.Copy(
          reference, (tile.Y + y) * pitch + tile.X,
          band.Buffer, (tile.Y + y) * pitch + tile.X,
          tile.Width);

      return;
    }

    var blocksPerMacroblock = macroblockSize != band.BlockSize ? 4 : 1;

    foreach (var macroblock in tile.Macroblocks) {
      int motionX = macroblock.MotionX;
      int motionY = macroblock.MotionY;
      var type = 0;

      if (band.IsHalfPel != 0) {
        type = ((motionY & 1) << 1) | (motionX & 1);
        motionX >>= 1;
        motionY >>= 1;
      }

      for (var block = 0; block < blocksPerMacroblock; ++block) {
        var at = macroblock.BufferOffset + band.BlockSize * ((block & 1) + ((block & 2) != 0 ? pitch : 0));
        this._Predict(band, at, motionX, motionY, 0, 0, type, -1, add: false);
      }
    }
  }

  private void _DecodeBlocks(IviBand band, IviTile tile) {
    var blockSize = band.BlockSize;
    var blocksPerMacroblock = band.MacroblockSize != blockSize ? 4 : 1;
    var pitch = band.Pitch;
    var previousDc = 0;

    foreach (var macroblock in tile.Macroblocks) {
      var isIntra = macroblock.Type == 0;
      var pattern = macroblock.CodedBlockPattern;
      var offset = macroblock.BufferOffset;

      var quantiser = band.GlobalQuantiser + macroblock.QuantiserDelta;
      var ceiling = this.IsIndeo4 ? 31 : 23;
      quantiser = quantiser < 0 ? 0 : quantiser > ceiling ? ceiling : quantiser;

      var scale = isIntra ? band.IntraScale : band.InterScale;
      if (scale != null)
        quantiser = scale[quantiser];

      int motionX = 0, motionY = 0, backwardX = 0, backwardY = 0;
      var type = 0;
      var backwardType = -1;

      if (!isIntra) {
        motionX = macroblock.MotionX;
        motionY = macroblock.MotionY;
        backwardX = macroblock.BackwardMotionX;
        backwardY = macroblock.BackwardMotionY;

        if (band.IsHalfPel != 0) {
          type = ((motionY & 1) << 1) | (motionX & 1);
          backwardType = ((backwardY & 1) << 1) | (backwardX & 1);
          motionX >>= 1;
          motionY >>= 1;
          backwardX >>= 1;
          backwardY >>= 1;
        }

        if (macroblock.Type == 2)
          type = -1;

        if (macroblock.Type is not (2 or 3))
          backwardType = -1;

        if (macroblock.Type != 0)
          _CheckMotionWithinBand(band, macroblock.X, macroblock.Y, macroblock.MotionX, macroblock.MotionY);

        if (macroblock.Type is 2 or 3)
          _CheckMotionWithinBand(band, macroblock.X, macroblock.Y, macroblock.BackwardMotionX, macroblock.BackwardMotionY);
      }

      for (var block = 0; block < blocksPerMacroblock; ++block) {
        if ((block & 1) != 0)
          offset += blockSize;
        else if (block == 2)
          offset += blockSize * pitch - blockSize;

        if ((pattern & 1) != 0)
          this._DecodeCodedBlock(
            band, offset, quantiser, isIntra, ref previousDc,
            motionX, motionY, backwardX, backwardY, type, backwardType);
        else {
          if ((blockSize - 1) * pitch + blockSize > band.Buffer.Length - offset)
            throw new InvalidDataException(
              "A block of this Indeo frame sits past the end of its band's buffer.");

          if (isIntra)
            band.DcTransform!(previousDc, band.Buffer, offset, pitch, blockSize);
          else
            this._Predict(band, offset, motionX, motionY, backwardX, backwardY, type, backwardType, add: false);
        }

        pattern >>= 1;
      }
    }

    this.Reader.Align();
  }

  private void _DecodeCodedBlock(
    IviBand band, int offset, int quantiser, bool isIntra, ref int previousDc,
    int motionX, int motionY, int backwardX, int backwardY, int type, int backwardType) {
    var weights = isIntra ? band.IntraBase : band.InterBase;
    var map = band.RunValueMap;
    var blockSize = band.BlockSize;
    var coefficientCount = blockSize * blockSize;
    var columnMask = blockSize - 1;
    var pitch = band.Pitch;

    if (band.Pitch * (band.TransformSize - 1) + band.TransformSize > band.Buffer.Length - offset)
      throw new InvalidDataException(
        "A coded block of this Indeo frame sits past the end of its band's buffer.");

    var coefficients = this._coefficients;
    Array.Clear(coefficients, 0, coefficientCount);
    var columnFlags = this._columnFlags;
    Array.Clear(columnFlags);

    var codebook = band.Codebook.Table
      ?? throw new InvalidDataException(
        "A band of this Indeo frame codes blocks without any header having said which codebook they are "
        + "coded with.");

    var scanPosition = -1;
    var symbol = 0;

    while (scanPosition <= coefficientCount) {
      symbol = codebook.Read(this.Reader);
      if (symbol == map.EndOfBlockSymbol)
        break;

      int run, value;
      if (symbol == map.EscapeSymbol) {
        run = codebook.Read(this.Reader) + 1;
        var low = codebook.Read(this.Reader);
        var high = codebook.Read(this.Reader);
        value = ToSigned((high << 6) | low);
      } else {
        run = map.Runs[symbol];
        value = map.Values[symbol];
      }

      scanPosition += run;
      if (scanPosition >= coefficientCount || scanPosition < 0)
        break;

      var at = band.Scan[scanPosition];
      var weight = (weights[at] * quantiser) >> 9;
      if (weight > 1)
        value = value * weight + (value > 0 ? 1 : -1) * (((weight ^ 1) - 1) >> 1);

      coefficients[at] = value;
      if (value != 0)
        columnFlags[at & columnMask] = 1;
    }

    if (scanPosition < 0 || (scanPosition >= coefficientCount && symbol != map.EndOfBlockSymbol))
      throw new InvalidDataException(
        "A coded block of this Indeo frame runs past its last coefficient without ending, which means the "
        + "block data has been read out of step with the bitstream.");

    // An intra block's DC coefficient is a difference from the block before it, and only where the
    // transform runs in both dimensions — a one-dimensional band codes its DC outright.
    if (isIntra && band.IsTwoDimensional) {
      previousDc += coefficients[0];
      coefficients[0] = previousDc;
      if (previousDc != 0)
        columnFlags[0] = 1;
    }

    if (band.TransformSize > blockSize)
      throw new InvalidDataException(
        $"A band of this Indeo frame states blocks of {blockSize} samples and a transform of "
        + $"{band.TransformSize}, which would read past the block it transforms.");

    band.InverseTransform!(coefficients, band.Buffer, offset, pitch, columnFlags);

    if (!isIntra)
      this._Predict(band, offset, motionX, motionY, backwardX, backwardY, type, backwardType, add: true);
  }

  /// <summary>
  /// Predicts one block from one reference or the average of two, adding to the residual already
  /// there or replacing it.
  /// </summary>
  private void _Predict(
    IviBand band, int offset, int motionX, int motionY, int backwardX, int backwardY,
    int type, int backwardType, bool add) {
    var pitch = band.Pitch;
    var size = band.BlockSize;
    var forwardOffset = offset + motionY * pitch + motionX;

    if (type != -1)
      _CheckPrediction(band, offset, forwardOffset, type, band.Reference);

    if (backwardType == -1) {
      IviMotionCompensation.Predict(
        band.Buffer, offset, pitch, band.Reference!, forwardOffset, pitch, size, type, add);
      return;
    }

    var backwardOffset = offset + backwardY * pitch + backwardX;
    _CheckPrediction(band, offset, backwardOffset, backwardType, band.BackwardReference);

    if (type == -1) {
      IviMotionCompensation.Predict(
        band.Buffer, offset, pitch, band.BackwardReference!, backwardOffset, pitch, size, backwardType, add);
      return;
    }

    IviMotionCompensation.PredictAverage(
      band.Buffer, offset, pitch,
      band.Reference!, forwardOffset, band.BackwardReference!, backwardOffset,
      size, type, backwardType, add);
  }

  /// <summary>
  /// Refuses a prediction that would read from outside the reference band buffer.
  /// </summary>
  /// <remarks>
  /// The allowance for a half-sample position is what makes this more than a range check: reading at
  /// a half-sample position reads one sample further right, one row further down, or both, and the
  /// last block of a band is exactly where that runs off the end.
  /// </remarks>
  private static void _CheckPrediction(IviBand band, int offset, int referenceOffset, int type, short[]? reference) {
    var span = band.Pitch * (band.BlockSize - 1) + band.BlockSize;
    var half = (type > 1 ? band.Pitch : 0) + (type & 1);

    if (reference == null || offset < 0 || referenceOffset < 0
        || band.BufferSize - span < offset
        || band.BufferSize - span - half < referenceOffset)
      throw new InvalidDataException(
        "A predicted block of this Indeo frame reads from outside the band it predicts from.");
  }

  /// <summary>
  /// Refuses a macroblock whose motion vector would move it outside the band, as the band's buffer
  /// is laid out.
  /// </summary>
  /// <remarks>
  /// Checked against the padded buffer and not against the band's stated width and height: a band's
  /// buffer is rounded up to whole macroblocks and a vector may legitimately point into that padding.
  /// This is the check the macroblock headers make; the tighter one below is what the blocks
  /// themselves are checked against, and the two are not the same test — this one bounds the
  /// macroblock's address within the buffer, that one bounds its position within the padded picture.
  /// </remarks>
  private protected static void CheckMotionWithinBuffer(IviBand band, int x, int y, int motionX, int motionY) {
    var half = band.IsHalfPel;
    var first = x + (motionX >> half) + (y + (motionY >> half)) * band.Pitch;
    var last = x + ((motionX + half) >> half) + band.MacroblockSize - 1
               + (y + band.MacroblockSize - 1 + ((motionY + half) >> half)) * band.Pitch;

    if (first < 0 || last > band.BufferSize - 1)
      throw new InvalidDataException(
        $"A macroblock of this Indeo frame has a motion vector of {motionX}, {motionY} at {x}, {y}, which "
        + "points outside the band it predicts from.");
  }

  /// <summary>Refuses a macroblock whose motion vector would move it outside the padded picture.</summary>
  private static void _CheckMotionWithinBand(IviBand band, int x, int y, int motionX, int motionY) {
    var half = band.IsHalfPel;
    var wholeX = motionX >> half;
    var wholeY = motionY >> half;
    var fractionX = motionX & half;
    var fractionY = motionY & half;

    if (x + wholeX < 0 || x + wholeX + band.MacroblockSize + fractionX > band.Pitch
        || y + wholeY < 0 || y + wholeY + band.MacroblockSize + fractionY > band.AlignedHeight)
      throw new InvalidDataException(
        $"A macroblock of this Indeo frame has a motion vector of {motionX}, {motionY} at {x}, {y}, which "
        + "moves it outside the band it predicts from.");
  }

  // ============================================================================================
  // Layout
  // ============================================================================================

  /// <summary>
  /// Builds the plane and band descriptors for a picture layout, discarding whatever was there.
  /// </summary>
  protected void InitializePlanes(IviPictureConfiguration configuration) {
    if (configuration.PictureWidth <= 0 || configuration.PictureHeight <= 0
        || configuration.LumaBands < 1 || configuration.ChromaBands < 1)
      throw new InvalidDataException(
        $"This Indeo picture header states a {configuration.PictureWidth}x{configuration.PictureHeight} "
        + $"picture in {configuration.LumaBands} luminance and {configuration.ChromaBands} chrominance "
        + "bands, which is not a picture.");

    // Indeo 4 states its picture size in two sixteen-bit fields, so a frame read out of step can ask
    // for a picture of four thousand million samples before anything else notices. The bound is this
    // decoder's and not the format's; it is well past what either format's own size table can name.
    if ((long)configuration.PictureWidth * configuration.PictureHeight > 1 << 26)
      throw new InvalidDataException(
        $"This Indeo picture header states a {configuration.PictureWidth}x{configuration.PictureHeight} "
        + "picture. That is larger than this decoder will allocate for, and no Indeo clip is anywhere "
        + "near it.");

    this.Planes[0].Width = configuration.PictureWidth;
    this.Planes[0].Height = configuration.PictureHeight;
    this.Planes[1].Width = this.Planes[2].Width = (configuration.PictureWidth + 3) >> 2;
    this.Planes[1].Height = this.Planes[2].Height = (configuration.PictureHeight + 3) >> 2;

    for (var p = 0; p < 3; ++p) {
      var plane = this.Planes[p];
      var bandCount = p == 0 ? configuration.LumaBands : configuration.ChromaBands;

      // One band is the whole plane; four are the quadrants of one wavelet decomposition, so each is
      // half the plane in each direction.
      var bandWidth = bandCount == 1 ? plane.Width : (plane.Width + 1) >> 1;
      var bandHeight = bandCount == 1 ? plane.Height : (plane.Height + 1) >> 1;

      var alignment = p == 0 ? 16 : 8;
      var pitch = (bandWidth + alignment - 1) / alignment * alignment;
      var alignedHeight = (bandHeight + alignment - 1) / alignment * alignment;

      plane.Bands = new IviBand[bandCount];
      for (var b = 0; b < bandCount; ++b)
        plane.Bands[b] = new() {
          Plane = p,
          BandNumber = b,
          Width = bandWidth,
          Height = bandHeight,
          Pitch = pitch,
          AlignedHeight = alignedHeight,
          BufferSize = pitch * alignedHeight,
        };
    }
  }

  /// <summary>
  /// Builds the tile and macroblock descriptors of every band, which is what a change of tiling or of
  /// block size forces.
  /// </summary>
  protected void InitializeTiles(int tileWidth, int tileHeight) {
    for (var p = 0; p < 3; ++p) {
      var width = p == 0 ? tileWidth : (tileWidth + 3) >> 2;
      var height = p == 0 ? tileHeight : (tileHeight + 3) >> 2;

      if (p == 0 && this.Planes[0].Bands.Length == 4) {
        if ((width & 1) != 0 || (height & 1) != 0)
          throw new NotSupportedException(
            $"This Indeo stream is scalable and tiles its luminance plane {width}x{height}. A scalable "
            + "picture halves its tiles along with its bands, and an odd tile cannot be halved.");

        width >>= 1;
        height >>= 1;
      }

      if (width <= 0 || height <= 0)
        throw new InvalidDataException(
          $"This Indeo picture header states tiles of {width}x{height} samples, which cover nothing.");

      foreach (var band in this.Planes[p].Bands) {
        var columns = (band.Width + width - 1) / width;
        var rows = (band.Height + height - 1) / height;
        band.Tiles = new IviTile[columns * rows];

        var reference = this.Planes[0].Bands[0].Tiles;
        var index = 0;

        for (var y = 0; y < band.Height; y += height)
          for (var x = 0; x < band.Width; x += width) {
            var tile = new IviTile {
              X = x,
              Y = y,
              MacroblockSize = band.MacroblockSize,
              Width = Math.Min(band.Width - x, width),
              Height = Math.Min(band.Height - y, height),
            };

            var count = _MacroblocksPerTile(tile.Width, tile.Height, band.MacroblockSize);
            tile.Macroblocks = new IviMacroblock[count];
            for (var i = 0; i < count; ++i)
              tile.Macroblocks[i] = new();

            if (p != 0 || band.BandNumber != 0) {
              if (index >= reference.Length || reference[index].Macroblocks.Length != count)
                throw new InvalidDataException(
                  $"Band {band.BandNumber} of plane {p} of this Indeo picture tiles into macroblocks that do "
                  + "not line up with the first luminance band's, which is what it inherits from.");

              tile.ReferenceMacroblocks = reference[index].Macroblocks;
            }

            band.Tiles[index] = tile;
            ++index;
          }
      }
    }
  }

  private static int _MacroblocksPerTile(int width, int height, int macroblockSize)
    => (width + macroblockSize - 1) / macroblockSize * ((height + macroblockSize - 1) / macroblockSize);

  // ============================================================================================
  // Shared header pieces
  // ============================================================================================

  /// <summary>
  /// Reads a codebook selection: one of the built-in eight, or a descriptor spelled out in full.
  /// </summary>
  /// <param name="coded">Whether the stream selects at all; where it does not, the eighth is used.</param>
  /// <param name="isBlockCodebook">Which of the two families of built-in descriptors to select from.</param>
  protected void ReadCodebook(IviCodebookSlot slot, bool coded, bool isBlockCodebook) {
    if (!coded) {
      slot.Table = _BuiltIn(isBlockCodebook, 7);
      return;
    }

    var selector = (int)this.Reader.Read(3);
    if (selector != 7) {
      slot.Table = _BuiltIn(isBlockCodebook, selector);
      return;
    }

    var rows = (int)this.Reader.Read(4);
    if (rows == 0)
      throw new InvalidDataException(
        "This Indeo header spells out a Huffman codebook with no rows, which stands for no codes at all.");

    var descriptor = new byte[rows];
    for (var i = 0; i < rows; ++i)
      descriptor[i] = (byte)this.Reader.Read(4);

    if (slot.CustomTable == null || !_SameDescriptor(slot.CustomDescriptor, descriptor)) {
      slot.CustomDescriptor = descriptor;
      slot.CustomTable = IviHuffmanTable.FromDescriptor(descriptor);
    }

    slot.Table = slot.CustomTable;
  }

  private static bool _SameDescriptor(byte[]? first, byte[] second) {
    if (first == null || first.Length != second.Length)
      return false;

    for (var i = 0; i < second.Length; ++i)
      if (first[i] != second[i])
        return false;

    return true;
  }

  private static IviHuffmanTable _BuiltIn(bool isBlockCodebook, int index)
    => isBlockCodebook ? _BlockTables.Value[index] : _MacroblockTables.Value[index];

  private static readonly Lazy<IviHuffmanTable[]> _MacroblockTables = new(() => _Build(IviTables.MacroblockDescriptors));

  private static readonly Lazy<IviHuffmanTable[]> _BlockTables = new(() => _Build(IviTables.BlockDescriptors));

  private static IviHuffmanTable[] _Build(byte[][] descriptors) {
    var tables = new IviHuffmanTable[descriptors.Length];
    for (var i = 0; i < descriptors.Length; ++i)
      tables[i] = IviHuffmanTable.FromDescriptor(descriptors[i]);

    return tables;
  }

  /// <summary>
  /// Skips a header extension, which is a chain of length-prefixed runs ending in a zero length.
  /// </summary>
  protected void SkipHeaderExtension() {
    int length;

    do {
      length = (int)this.Reader.Read(8);
      if (length * 8 > this.Reader.BitsLeft)
        throw new InvalidDataException(
          $"A header extension of this Indeo frame states {length} bytes and the frame has "
          + $"{Math.Max(this.Reader.BitsLeft, 0) / 8} left.");

      this.Reader.Skip(length * 8);
    } while (length != 0);
  }

  /// <summary>
  /// Turns a value whose sign is in its lowest bit into a signed one.
  /// </summary>
  /// <remarks>
  /// Both formats code every signed quantity this way — quantiser deltas, motion vector deltas, and
  /// the value of an escaped coefficient — so that a codebook can put the small magnitudes first
  /// regardless of sign. Zero is even and maps to zero; the mapping is otherwise
  /// 1 → 1, 2 → -1, 3 → 2, 4 → -2, so it is not two's complement and not a sign-magnitude form either.
  /// </remarks>
  private protected static int ToSigned(int value) => (value & 1) == 0 ? -(value >> 1) : (value >> 1) + 1;

  /// <summary>
  /// Scales an inherited motion vector down to a band whose macroblocks are smaller, rounding
  /// towards zero.
  /// </summary>
  private protected static sbyte ScaleMotion(int motion, int scale)
    => scale == 0 ? (sbyte)motion : (sbyte)((motion + (motion > 0 ? 1 : 0) + (scale - 1)) >> scale);
}

/// <summary>
/// Where a codebook selection is remembered between the header that made it and the blocks that use
/// it.
/// </summary>
/// <remarks>
/// A custom codebook is kept alongside its descriptor so that a stream restating the same descriptor
/// every frame — which they do — does not rebuild the same lookup every frame.
/// </remarks>
internal sealed class IviCodebookSlot {

  internal IviHuffmanTable? Table;

  internal byte[]? CustomDescriptor;

  internal IviHuffmanTable? CustomTable;
}
