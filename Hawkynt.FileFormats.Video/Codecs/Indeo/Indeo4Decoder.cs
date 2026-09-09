using System;
using System.IO;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Indeo 4's picture and band headers, its bidirectional frames, and the two frames it packs into one
/// packet.
/// </summary>
/// <remarks>
/// Indeo 4 states the whole picture layout in every picture header, where Indeo 5 states it once per
/// group of pictures, and it lets a band choose its transform, its scan pattern and its quantisation
/// matrix rather than deriving them from the band's position. That is the whole of what makes it a
/// different format: below the band header the two are one codec.
/// <para/>
/// <b>Bidirectional frames arrive backwards, and inside another frame.</b> Indeo 4 can code a frame
/// that predicts from both the picture before it and the picture after it, which means the later
/// picture has to be decoded first. What the format does about that is to put the intra frame and the
/// predicted frame that follows it in the <i>same packet</i>, one after the other with a version
/// string between them, and then to send an empty frame later on where the second one belongs. This
/// decoder does the same: an intra frame that has a second frame behind it decodes both, hands back
/// the first, and holds the second until the empty frame asks for it. Intel's own decoders did this
/// and there is no other way to read such a stream.
/// </remarks>
internal sealed class Indeo4Decoder : IviDecoder {

  internal const int FrameTypeIntra = 0;

  /// <summary>An intra frame whose macroblock types are coded slightly differently.</summary>
  internal const int FrameTypeIntra1 = 1;

  internal const int FrameTypeInter = 2;
  internal const int FrameTypeBidirectional = 3;
  internal const int FrameTypeInterNoReference = 4;
  internal const int FrameTypeNullFirst = 5;
  internal const int FrameTypeNullLast = 6;

  /// <summary>The size index that means the picture size follows in full.</summary>
  private const int _PICTURE_SIZE_ESCAPE = 7;

  /// <summary>The picture start code, as the little-endian reader delivers its eighteen bits.</summary>
  private const uint _PICTURE_START_CODE = 0x3FFF8;

  /// <summary>
  /// The picture start code with a frame type of "inter" behind it, which is how a second frame packed
  /// into the same packet is recognised.
  /// </summary>
  private const uint _TRAILING_INTER_FRAME = 0xBFFF8;

  /// <summary>Whether a quantiser delta is coded for every macroblock rather than only coded ones.</summary>
  private bool _quantiserDeltaCoded;

  private IviPicture? _deferred;

  internal Indeo4Decoder() {
    this.DestinationBuffer = 0;
    this.ReferenceBuffer = 1;

    // Buffer two belongs to the scalable mode, so the second reference of a bidirectional frame is
    // the fourth.
    this.BackwardReferenceBuffer = 3;
  }

  protected override bool IsIndeo4 => true;

  protected override bool IsNonNullFrame => this.FrameType < FrameTypeNullFirst;

  protected override bool IsBidirectionalFrame => this.FrameType == FrameTypeBidirectional;

  // ============================================================================================
  // Picture header
  // ============================================================================================

  protected override void DecodePictureHeader() {
    if (this.Reader.Read(18) != _PICTURE_START_CODE)
      throw new InvalidDataException(
        "This Indeo 4 frame does not begin with the eighteen-bit picture start code, so it is not the "
        + "start of a frame.");

    this.PreviousFrameType = this.FrameType;
    this.FrameType = (int)this.Reader.Read(3);
    if (this.FrameType == 7)
      throw new InvalidDataException(
        "This Indeo 4 frame states a frame type of 7, and the format defines seven types.");

    this.Reader.Skip(1); // Whether the clip uses transparency, which does not change the decode.

    if (this.Reader.ReadFlag())
      throw new InvalidDataException(
        "This Indeo 4 picture header has its reserved bit set. Intel's Macintosh decoder ignored it and "
        + "XAnim refused the frame; nothing states what a stream setting it means.");

    if (this.Reader.ReadFlag())
      this.Reader.Skip(24); // The picture data size, which the bands account for themselves.

    // A null frame carries nothing else at all.
    if (this.FrameType >= FrameTypeNullFirst)
      return;

    // The lock word of a password-protected clip. Indeo 4 scrambles nothing, so the word is a key
    // check and the picture decodes without it.
    if (this.Reader.ReadFlag())
      this.Reader.Skip(32);

    int width, height;
    var sizeIndex = (int)this.Reader.Read(3);
    if (sizeIndex == _PICTURE_SIZE_ESCAPE) {
      height = (int)this.Reader.Read(16);
      width = (int)this.Reader.Read(16);
    } else {
      height = Indeo4Tables.CommonPictureSizes[sizeIndex * 2 + 1];
      width = Indeo4Tables.CommonPictureSizes[sizeIndex * 2];
    }

    int tileWidth, tileHeight;
    if (this.Reader.ReadFlag()) {
      tileHeight = _ScaleTileSize(height, (int)this.Reader.Read(4));
      tileWidth = _ScaleTileSize(width, (int)this.Reader.Read(4));
    } else {
      tileHeight = height;
      tileWidth = width;
    }

    if (this.Reader.Read(2) != 0)
      throw new NotSupportedException(
        "This Indeo 4 clip states a chrominance subsampling other than YVU9. Indeo 4 was specified for "
        + "YVU9 and no encoder is known to have written another, so what its band layout would be has "
        + "never been observed.");

    var lumaBands = this._ReadPlaneSubdivision();
    var chromaBands = lumaBands != 0 ? this._ReadPlaneSubdivision() : 0;

    var isScalable = lumaBands != 1 || chromaBands != 1;
    if (isScalable && (lumaBands != 4 || chromaBands != 1))
      throw new InvalidDataException(
        $"This Indeo 4 picture header states {lumaBands} luminance and {chromaBands} chrominance bands. "
        + "The only subdivision the format defines is one band or four for luminance against one for "
        + "chrominance.");

    this.IsScalable = isScalable;

    var configuration = new IviPictureConfiguration(
      PictureWidth: width,
      PictureHeight: height,
      ChromaWidth: (width + 3) >> 2,
      ChromaHeight: (height + 3) >> 2,
      TileWidth: tileWidth,
      TileHeight: tileHeight,
      LumaBands: lumaBands,
      ChromaBands: chromaBands);

    if (configuration != this.PictureConfiguration) {
      this.InitializePlanes(configuration);
      this.PictureConfiguration = configuration;

      // The default block geometry, which a band header may then change. A scalable picture halves
      // its luminance macroblocks along with its bands.
      for (var p = 0; p < 3; ++p)
        foreach (var band in this.Planes[p].Bands) {
          band.MacroblockSize = p == 0 ? isScalable ? 8 : 16 : 4;
          band.BlockSize = p == 0 ? 8 : 4;
        }

      this.InitializeTiles(tileWidth, tileHeight);
    }

    if (this.Reader.ReadFlag())
      this.Reader.Skip(20); // The frame number.

    if (this.Reader.ReadFlag())
      this.Reader.Skip(8); // An estimate of how long the frame takes to decode.

    var macroblockCodebookCoded = this.Reader.ReadFlag();
    this.ReadCodebook(this.MacroblockCodebook, macroblockCodebookCoded, isBlockCodebook: false);
    var blockCodebookCoded = this.Reader.ReadFlag();
    this.ReadCodebook(this.BlockCodebook, blockCodebookCoded, isBlockCodebook: true);

    // The picture's run-value map selector, which every band restates for itself.
    if (this.Reader.ReadFlag())
      this.Reader.Skip(3);

    this.Reader.Skip(1); // Whether the clip is interleaved, which does not change the decode.
    this._quantiserDeltaCoded = this.Reader.ReadFlag();

    this.Reader.Skip(5); // The picture quantiser, which every band restates for itself.

    if (this.Reader.ReadFlag())
      this.Reader.Skip(3);

    if (this.Reader.ReadFlag())
      this.Reader.Skip(16); // The picture checksum.

    while (this.Reader.ReadFlag()) {
      if (this.Reader.BitsLeft < 10)
        throw new InvalidDataException(
          "This Indeo 4 picture header ends in the middle of a header extension.");

      this.Reader.Skip(8);
    }

    // The "bad blocks" bit, which says the encoder knew some blocks were wrong. It changes nothing
    // about how they are read.
    this.Reader.Skip(1);

    this.Reader.Align();
  }

  /// <summary>
  /// Reads how a plane is subdivided, which is either one band or four and is coded as a tree.
  /// </summary>
  /// <remarks>
  /// The code is a quadtree that could in principle describe any subdivision; the two the format
  /// actually defines are a leaf, meaning one band, and one split into four leaves, meaning four. A
  /// deeper tree describes a subdivision no transform exists for.
  /// </remarks>
  private int _ReadPlaneSubdivision() {
    switch (this.Reader.Read(2)) {
      case 3:
        return 1;
      case 2:
        for (var i = 0; i < 4; ++i)
          if (this.Reader.Read(2) != 3)
            return 0;

        return 4;
      default:
        return 0;
    }
  }

  private static int _ScaleTileSize(int pictureSize, int factor) => factor == 15 ? pictureSize : (factor + 1) << 5;

  // ============================================================================================
  // Band header
  // ============================================================================================

  protected override void DecodeBandHeader(IviBand band) {
    var plane = (int)this.Reader.Read(2);
    var number = (int)this.Reader.Read(4);

    if (band.Plane != plane || band.BandNumber != number)
      throw new InvalidDataException(
        $"This Indeo 4 frame names band {number} of plane {plane} where band {band.BandNumber} of plane "
        + $"{band.Plane} was due, so the bands are not in the order the picture header stated.");

    band.IsEmpty = this.Reader.ReadFlag();
    if (!band.IsEmpty)
      this._DecodeBandBody(band);

    var matrix = Indeo4Tables.QuantIndexToTable[band.QuantiserMatrix];
    if (band.BlockSize == 8) {
      band.IntraBase = Indeo4Tables.Quant8x8Intra[matrix];
      band.InterBase = Indeo4Tables.Quant8x8Inter[matrix];
    } else {
      band.IntraBase = Indeo4Tables.Quant4x4Intra[matrix];
      band.InterBase = Indeo4Tables.Quant4x4Inter[matrix];
    }

    // Indeo 4 multiplies its matrix by the quantiser directly, where Indeo 5 looks the quantiser up
    // in a scale table first.
    band.IntraScale = null;
    band.InterScale = null;

    this.Reader.Align();

    if (band.Scan.Length == 0)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame inherits a scan pattern from "
        + "an earlier frame that never stated one.");
  }

  private void _DecodeBandBody(IviBand band) {
    var previousBlockSize = band.BlockSize;

    if (this.Reader.ReadFlag())
      this.Reader.Skip(16); // The band header's size, which is four bytes when it is not stated.

    band.IsHalfPel = (int)this.Reader.Read(2);
    if (band.IsHalfPel >= 2)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame states motion vector "
        + $"resolution {band.IsHalfPel}, and the format defines whole samples and half samples.");

    band.ChecksumPresent = this.Reader.ReadFlag();
    if (band.ChecksumPresent)
      band.Checksum = (int)this.Reader.Read(16);

    var geometry = (int)this.Reader.Read(2);
    if (geometry == 3)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame states a block size the "
        + "format leaves undefined.");

    band.MacroblockSize = 16 >> geometry;
    band.BlockSize = 8 >> (geometry >> 1);

    band.InheritMotionVectors = this.Reader.ReadFlag();
    band.InheritQuantiserDelta = this.Reader.ReadFlag();
    band.GlobalQuantiser = (int)this.Reader.Read(5);

    // A band either states its transform, scan and quantisation matrix or inherits the ones it had in
    // the previous frame. An intra frame always states them, whatever the bit says.
    if (!this.Reader.ReadFlag() || this.FrameType == FrameTypeIntra)
      this._DecodeBandTransform(band);
    else if (previousBlockSize != band.BlockSize)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame inherits a transform built "
        + $"for blocks of {previousBlockSize} samples while stating blocks of {band.BlockSize}.");

    if (Indeo4Tables.QuantIndexToTable[band.QuantiserMatrix] > 4 && band.BlockSize == 4) {
      band.QuantiserMatrix = 0;
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame selects an eight-by-eight "
        + "dequantisation matrix for four-by-four blocks.");
    }

    if (band.ScanSize != band.BlockSize)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame inherits a scan pattern for "
        + $"blocks of {band.ScanSize} samples while stating blocks of {band.BlockSize}.");

    if (band.TransformSize == 8 && band.BlockSize < 8)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame inherits an eight-by-eight "
        + $"transform while stating blocks of {band.BlockSize} samples.");

    // A band either uses the picture's block codebook or spells out one of its own.
    if (!this.Reader.ReadFlag())
      band.Codebook.Table = this.BlockCodebook.Table;
    else
      this.ReadCodebook(band.Codebook, coded: true, isBlockCodebook: true);

    band.RunValueMapSelector = this.Reader.ReadFlag() ? (int)this.Reader.Read(3) : 8;

    band.CorrectionCount = 0;
    if (this.Reader.ReadFlag()) {
      band.CorrectionCount = (int)this.Reader.Read(8);
      if (band.CorrectionCount > 61)
        throw new InvalidDataException(
          $"A band of this Indeo 4 frame states {band.CorrectionCount} run-value corrections, and the "
          + "format allows 61.");

      for (var i = 0; i < band.CorrectionCount * 2; ++i)
        band.Corrections[i] = (byte)this.Reader.Read(8);
    }
  }

  /// <summary>
  /// Reads the transform, scan pattern and dequantisation matrix a band states for itself.
  /// </summary>
  private void _DecodeBandTransform(IviBand band) {
    var transform = (int)this.Reader.Read(5);

    if (transform >= _Transforms.Length || _Transforms[transform].Inverse == null)
      throw new NotSupportedException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 clip selects transform {transform}. "
        + "The format numbers eighteen transforms and five of them — the four discrete cosine transforms "
        + "and the four-by-four pass-through — Intel's own decoders never implemented, so no clip carries "
        + "one and what they would produce has never been observed.");

    if (transform < 10 && band.BlockSize < 8)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame selects an eight-by-eight "
        + $"transform for blocks of {band.BlockSize} samples.");

    band.InverseTransform = _Transforms[transform].Inverse;
    band.DcTransform = _Transforms[transform].Dc;
    band.IsTwoDimensional = _Transforms[transform].IsTwoDimensional;
    band.TransformSize = transform < 10 ? 8 : 4;

    if (band.TransformSize != band.BlockSize)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame states blocks of "
        + $"{band.BlockSize} samples and a transform of {band.TransformSize}.");

    var scan = (int)this.Reader.Read(4);
    if (scan == 15)
      throw new NotSupportedException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 clip spells out a scan pattern of "
        + "its own. The format reserves the index for one and no clip is known to carry one, so where in "
        + "the header it would sit has never been observed.");

    var wantsFourByFour = scan is > 4 and < 10;
    if (wantsFourByFour ? band.BlockSize != 4 : band.BlockSize != 8)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame selects scan pattern {scan}, "
        + $"which is for blocks of {(wantsFourByFour ? 4 : 8)} samples, and states blocks of "
        + $"{band.BlockSize}.");

    band.Scan = _Scans[scan];
    band.ScanSize = band.BlockSize;

    var matrix = (int)this.Reader.Read(5);
    if (matrix == 31)
      throw new NotSupportedException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 clip spells out a dequantisation "
        + "matrix of its own. The format reserves the index for one and no clip is known to carry one, so "
        + "where in the header it would sit has never been observed.");

    if (matrix >= Indeo4Tables.QuantIndexToTable.Length)
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame selects dequantisation "
        + $"matrix {matrix}, and the format numbers {Indeo4Tables.QuantIndexToTable.Length}.");

    band.QuantiserMatrix = matrix;
  }

  // ============================================================================================
  // Macroblocks
  // ============================================================================================

  protected override void DecodeMacroblockInfo(IviBand band, IviTile tile) {
    var reference = tile.ReferenceMacroblocks;
    var blocksPerMacroblock = band.MacroblockSize != band.BlockSize ? 4 : 1;
    var typeBits = this.FrameType == FrameTypeBidirectional ? 2 : 1;
    var motionScale = (this.Planes[0].Bands[0].MacroblockSize >> 3) - (band.MacroblockSize >> 3);

    var expected = (tile.Width + band.MacroblockSize - 1) / band.MacroblockSize
                   * ((tile.Height + band.MacroblockSize - 1) / band.MacroblockSize);
    if (tile.Macroblocks.Length != expected)
      throw new InvalidDataException(
        $"A tile of band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame is "
        + $"{tile.Width}x{tile.Height} at macroblocks of {band.MacroblockSize}, which is {expected} "
        + $"macroblocks and not the {tile.Macroblocks.Length} it was built with.");

    var index = 0;
    var offset = tile.Y * band.Pitch + tile.X;
    int motionX = 0, motionY = 0;

    for (var y = tile.Y; y < tile.Y + tile.Height; y += band.MacroblockSize) {
      var macroblockOffset = offset;

      for (var x = tile.X; x < tile.X + tile.Width; x += band.MacroblockSize) {
        var macroblock = tile.Macroblocks[index];
        var source = reference?[index];

        macroblock.X = x;
        macroblock.Y = y;
        macroblock.BufferOffset = macroblockOffset;
        macroblock.BackwardMotionX = 0;
        macroblock.BackwardMotionY = 0;

        if (this.Reader.BitsLeft < 1)
          throw new InvalidDataException(
            "This Indeo 4 frame ends in the middle of the macroblock headers of a tile.");

        if (this.Reader.ReadFlag()) {
          if (this.FrameType == FrameTypeIntra)
            throw new InvalidDataException(
              "This Indeo 4 intra frame holds a macroblock that says it repeats the reference frame, and "
              + "an intra frame has no reference frame.");

          macroblock.Type = 1;
          macroblock.CodedBlockPattern = 0;
          macroblock.QuantiserDelta = 0;

          if (band.Plane == 0 && band.BandNumber == 0 && this._quantiserDeltaCoded)
            macroblock.QuantiserDelta = (sbyte)ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));

          macroblock.MotionX = 0;
          macroblock.MotionY = 0;

          if (band.InheritMotionVectors && source != null) {
            macroblock.MotionX = ScaleMotion(source.MotionX, motionScale);
            macroblock.MotionY = ScaleMotion(source.MotionY, motionScale);
          }
        } else {
          if (band.InheritMotionVectors) {
            if (source == null)
              throw new InvalidDataException(
                $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 4 frame says its macroblocks "
                + "inherit from the first luminance band, and it is the first luminance band.");

            macroblock.Type = source.Type;
          } else if (this.FrameType is FrameTypeIntra or FrameTypeIntra1)
            macroblock.Type = 0;
          else
            macroblock.Type = (byte)this.Reader.Read(typeBits);

          macroblock.CodedBlockPattern = (byte)this.Reader.Read(blocksPerMacroblock);

          macroblock.QuantiserDelta = 0;
          if (band.InheritQuantiserDelta) {
            if (source != null)
              macroblock.QuantiserDelta = source.QuantiserDelta;
          } else if (macroblock.CodedBlockPattern != 0
                     || (band.Plane == 0 && band.BandNumber == 0 && this._quantiserDeltaCoded))
            macroblock.QuantiserDelta = (sbyte)ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));

          if (macroblock.Type == 0) {
            macroblock.MotionX = 0;
            macroblock.MotionY = 0;
          } else if (band.InheritMotionVectors) {
            if (source != null) {
              macroblock.MotionX = ScaleMotion(source.MotionX, motionScale);
              macroblock.MotionY = ScaleMotion(source.MotionY, motionScale);
            }
          } else {
            motionY += ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));
            motionX += ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));
            macroblock.MotionX = (sbyte)motionX;
            macroblock.MotionY = (sbyte)motionY;

            if (macroblock.Type == 3) {
              motionY += ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));
              motionX += ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));
              macroblock.BackwardMotionX = (sbyte)-motionX;
              macroblock.BackwardMotionY = (sbyte)-motionY;
            }
          }

          // A backward-predicted macroblock states its vector as the forward one and then negates it.
          if (macroblock.Type == 2) {
            macroblock.BackwardMotionX = (sbyte)-macroblock.MotionX;
            macroblock.BackwardMotionY = (sbyte)-macroblock.MotionY;
            macroblock.MotionX = 0;
            macroblock.MotionY = 0;
          }
        }

        if (macroblock.Type != 0)
          CheckMotionWithinBuffer(band, x, y, macroblock.MotionX, macroblock.MotionY);

        ++index;
        macroblockOffset += band.MacroblockSize;
      }

      offset += band.MacroblockSize * band.Pitch;
    }

    this.Reader.Align();
  }

  // ============================================================================================
  // Buffers and the frame packed behind a frame
  // ============================================================================================

  protected override void SwitchBuffers() {
    var wasReference = this.PreviousFrameType is FrameTypeIntra or FrameTypeIntra1 or FrameTypeInter;
    var isReference = this.FrameType is FrameTypeIntra or FrameTypeIntra1 or FrameTypeInter;

    if (wasReference && isReference)
      (this.DestinationBuffer, this.ReferenceBuffer) = (this.ReferenceBuffer, this.DestinationBuffer);
    else if (wasReference) {
      (this.ReferenceBuffer, this.BackwardReferenceBuffer) = (this.BackwardReferenceBuffer, this.ReferenceBuffer);
      (this.DestinationBuffer, this.ReferenceBuffer) = (this.ReferenceBuffer, this.DestinationBuffer);
    }
  }

  protected override bool TakeDeferredPicture(out IviPicture? picture) {
    picture = null;
    if (this.FrameType != FrameTypeNullLast)
      return false;

    picture = this._deferred;
    this._deferred = null;
    return true;
  }

  /// <summary>
  /// Decodes the predicted frame packed behind an intra frame, if there is one, and holds it for the
  /// empty frame that will ask for it.
  /// </summary>
  /// <remarks>
  /// What separates the two frames is a version string: a run of bytes ending in a zero, after which
  /// the second frame starts at the next multiple of eight bytes. Whether there is a second frame at
  /// all is decided by looking for the picture start code with an "inter" frame type behind it,
  /// because nothing in the first frame says how many frames the packet holds.
  /// </remarks>
  protected override void DeferTrailingPicture() {
    if (this.FrameType != FrameTypeIntra)
      return;

    var reader = this.Reader;

    while (reader.Read(8) != 0)
      if (reader.BitsLeft < 8)
        return;

    reader.Skip(64 - (reader.Position & 0x18));

    if (reader.BitsLeft <= 18 || reader.Peek(21) != _TRAILING_INTER_FRAME)
      return;

    // The second frame is a predicted one, so nothing it decodes can lead back here.
    var packet = reader.Remaining;

    try {
      this._deferred = this.Decode(packet);
    } catch (Exception failure) when (failure is InvalidDataException or NotSupportedException) {
      // Trailing bytes that look like a frame and are not cost nothing: the frame this packet was
      // decoded for has already been produced, and the empty frame that would have asked for a second
      // one gets nothing instead.
      this._deferred = null;
    }
  }

  /// <summary>
  /// The eighteen transforms an Indeo 4 band header may select by index, four of which are the
  /// discrete cosine transform that Intel specified and never shipped.
  /// </summary>
  private static readonly (IviInverseTransform? Inverse, IviDcTransform? Dc, bool IsTwoDimensional)[] _Transforms = [
    (IviTransforms.InverseHaar8x8, IviTransforms.DcHaar2D, true),
    (IviTransforms.RowHaar8, IviTransforms.DcHaar2D, false),
    (IviTransforms.ColumnHaar8, IviTransforms.DcHaar2D, false),
    (IviTransforms.PutPixels8x8, IviTransforms.PutDcPixel8x8, true),
    (IviTransforms.InverseSlant8x8, IviTransforms.DcSlant2D, true),
    (IviTransforms.RowSlant8, IviTransforms.DcRowSlant, true),
    (IviTransforms.ColumnSlant8, IviTransforms.DcColumnSlant, true),
    (null, null, false), // Inverse discrete cosine transform, eight by eight.
    (null, null, false), // Inverse discrete cosine transform, eight by one.
    (null, null, false), // Inverse discrete cosine transform, one by eight.
    (IviTransforms.InverseHaar4x4, IviTransforms.DcHaar2D, true),
    (IviTransforms.InverseSlant4x4, IviTransforms.DcSlant2D, true),
    (null, null, false), // No transform, four by four.
    (IviTransforms.RowHaar4, IviTransforms.DcHaar2D, false),
    (IviTransforms.ColumnHaar4, IviTransforms.DcHaar2D, false),
    (IviTransforms.RowSlant4, IviTransforms.DcRowSlant, false),
    (IviTransforms.ColumnSlant4, IviTransforms.DcColumnSlant, false),
    (null, null, false), // Inverse discrete cosine transform, four by four.
  ];

  /// <summary>The fifteen scan patterns an Indeo 4 band header may select by index.</summary>
  private static readonly byte[][] _Scans = [
    // For eight-by-eight transforms.
    IviTables.ZigzagDirect,
    Indeo4Tables.AlternateScan8x8,
    IviTables.HorizontalScan8x8,
    IviTables.VerticalScan8x8,
    IviTables.ZigzagDirect,

    // For four-by-four transforms.
    IviTables.DirectScan4x4,
    Indeo4Tables.AlternateScan4x4,
    Indeo4Tables.VerticalScan4x4,
    Indeo4Tables.HorizontalScan4x4,
    IviTables.DirectScan4x4,

    // The rest are unreachable for want of a block size they would fit.
    IviTables.HorizontalScan8x8,
    IviTables.HorizontalScan8x8,
    IviTables.HorizontalScan8x8,
    IviTables.HorizontalScan8x8,
    IviTables.HorizontalScan8x8,
  ];
}
