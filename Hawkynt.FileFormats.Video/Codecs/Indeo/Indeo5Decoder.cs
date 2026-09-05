using System;
using System.IO;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Indeo 5's three headers — group of pictures, picture and band — and the buffer rotation that goes
/// with its frame types.
/// </summary>
/// <remarks>
/// Indeo 5 states the picture layout once per group of pictures rather than once per frame. A key
/// frame carries a group header giving the picture size, the tiling, the number of wavelet bands and,
/// for each band, its transform, its block size and its quantisation matrix; every frame until the
/// next key frame inherits all of it. A stream that opens on a predicted frame therefore cannot be
/// decoded at all, and one whose group header is unreadable loses every frame of the group rather
/// than one.
/// <para/>
/// The transform a band uses is not stated by the band. It follows from which plane and which band
/// number it is, through the fixed mapping below: the first luminance band gets the two-dimensional
/// slant transform, the second the row transform, the third the column transform, the fourth no
/// transform at all, and chrominance the four-by-four slant. That is why an Indeo 5 band header is so
/// short and why a two-byte frame is a complete frame.
/// </remarks>
internal sealed class Indeo5Decoder : IviDecoder {

  internal const int FrameTypeIntra = 0;
  internal const int FrameTypeInter = 1;
  internal const int FrameTypeInterScalable = 2;
  internal const int FrameTypeInterNoReference = 3;
  internal const int FrameTypeNull = 4;

  /// <summary>The size index that means the picture size follows in full.</summary>
  private const int _PICTURE_SIZE_ESCAPE = 15;

  private int _frameFlags;

  private int _bufferSwitch;

  private bool _scalableSequence;

  internal Indeo5Decoder(int width, int height) {
    this.GroupInvalid = true;

    // The basic profile until a group header says otherwise: one band per plane, one tile, YVU9.
    this.PictureConfiguration = new(
      PictureWidth: width,
      PictureHeight: height,
      ChromaWidth: (width + 3) >> 2,
      ChromaHeight: (height + 3) >> 2,
      TileWidth: width,
      TileHeight: height,
      LumaBands: 1,
      ChromaBands: 1);

    this.InitializePlanes(this.PictureConfiguration);
  }

  protected override bool IsIndeo4 => false;

  protected override bool IsNonNullFrame => this.FrameType != FrameTypeNull;

  // ============================================================================================
  // Picture header
  // ============================================================================================

  protected override void DecodePictureHeader() {
    if (this.Reader.Read(5) != 0x1F)
      throw new InvalidDataException(
        "This Indeo 5 frame does not begin with the five-bit picture start code, so it is not the start "
        + "of a frame.");

    this.PreviousFrameType = this.FrameType;
    this.FrameType = (int)this.Reader.Read(3);
    if (this.FrameType > FrameTypeNull) {
      this.FrameType = FrameTypeIntra;
      throw new InvalidDataException(
        "This Indeo 5 frame states a frame type of 5 or 6, and the format defines five types.");
    }

    this.Reader.Skip(8); // The frame number, which nothing here needs.

    if (this.FrameType == FrameTypeIntra) {
      try {
        this._DecodeGroupHeader();
      } catch {
        // Every frame until the next key frame depends on this header, so one that cannot be read
        // costs the whole group and not one frame.
        this.GroupInvalid = true;
        throw;
      }

      this.GroupInvalid = false;
    }

    if (this.FrameType == FrameTypeInterScalable && !this.IsScalable) {
      this.FrameType = FrameTypeInter;
      throw new InvalidDataException(
        "This Indeo 5 frame is a scalable predicted frame in a stream whose group header states one band "
        + "per plane, so there are no higher bands for it to refine.");
    }

    if (this.FrameType != FrameTypeNull) {
      this._frameFlags = (int)this.Reader.Read(8);

      if ((this._frameFlags & 1) != 0)
        this.Reader.Skip(24); // The picture data size, which the bands account for themselves.

      if ((this._frameFlags & 0x10) != 0)
        this.Reader.Skip(16); // The picture checksum.

      if ((this._frameFlags & 0x20) != 0)
        this.SkipHeaderExtension();

      this.ReadCodebook(this.MacroblockCodebook, (this._frameFlags & 0x40) != 0, isBlockCodebook: false);
      this.Reader.Skip(3);
    }

    this.Reader.Align();
  }

  /// <summary>
  /// Reads the group header a key frame carries, which states the picture layout every frame until
  /// the next key frame will use.
  /// </summary>
  private void _DecodeGroupHeader() {
    var flags = (int)this.Reader.Read(8);

    if ((flags & 1) != 0)
      this.Reader.Skip(16); // The group header's own size.

    this.IsProtected = (flags & 0x20) != 0;
    if (this.IsProtected)
      this.Reader.Skip(32); // The lock word, which is the key check and not the key.

    var tileSize = (flags & 0x40) != 0 ? 64 << (int)this.Reader.Read(2) : 0;
    if (tileSize > 256)
      throw new InvalidDataException(
        $"This Indeo 5 group header states tiles of {tileSize} samples, and the two-bit field it comes "
        + "from cannot mean more than 256.");

    var lumaBands = (int)this.Reader.Read(2) * 3 + 1;
    var chromaBands = this.Reader.ReadBit() * 3 + 1;
    var isScalable = lumaBands != 1 || chromaBands != 1;

    if (isScalable && (lumaBands != 4 || chromaBands != 1))
      throw new InvalidDataException(
        $"This Indeo 5 group header states {lumaBands} luminance and {chromaBands} chrominance bands. "
        + "The only subdivision the format defines is one band or four for luminance against one for "
        + "chrominance.");

    int width, height;
    var sizeIndex = (int)this.Reader.Read(4);
    if (sizeIndex == _PICTURE_SIZE_ESCAPE) {
      height = (int)this.Reader.Read(13);
      width = (int)this.Reader.Read(13);
    } else {
      height = Indeo5Tables.CommonPictureSizes[sizeIndex * 2 + 1] << 2;
      width = Indeo5Tables.CommonPictureSizes[sizeIndex * 2] << 2;
    }

    if ((flags & 2) != 0)
      throw new NotSupportedException(
        "This Indeo 5 clip states the YV12 picture format. Indeo 5 was specified for YVU9 and no encoder "
        + "is known to have written the other, so what its band layout would be has never been observed.");

    var configuration = new IviPictureConfiguration(
      PictureWidth: width,
      PictureHeight: height,
      ChromaWidth: (width + 3) >> 2,
      ChromaHeight: (height + 3) >> 2,
      TileWidth: tileSize == 0 ? width : tileSize,
      TileHeight: tileSize == 0 ? height : tileSize,
      LumaBands: lumaBands,
      ChromaBands: chromaBands);

    var layoutChanged = false;
    if (configuration != this.PictureConfiguration || this.GroupInvalid) {
      this.InitializePlanes(configuration);
      this.PictureConfiguration = configuration;
      this.IsScalable = isScalable;
      layoutChanged = true;
    }

    for (var p = 0; p <= 1; ++p) {
      var bandCount = p == 0 ? lumaBands : chromaBands;

      for (var i = 0; i < bandCount; ++i) {
        var band = this.Planes[p].Bands[i];

        band.IsHalfPel = this.Reader.ReadBit();

        var wholeMacroblock = this.Reader.ReadBit();
        var blockSize = 8 >> this.Reader.ReadBit();
        var macroblockSize = blockSize << (wholeMacroblock == 0 ? 1 : 0);

        if (p == 0 && blockSize == 4)
          throw new NotSupportedException(
            "This Indeo 5 clip codes its luminance in four-by-four blocks. The format allows it and no "
            + "encoder is known to have written it, so what its scan and quantisation would be has never "
            + "been observed.");

        layoutChanged = macroblockSize != band.MacroblockSize || blockSize != band.BlockSize;
        if (layoutChanged) {
          band.MacroblockSize = macroblockSize;
          band.BlockSize = blockSize;
        }

        if (this.Reader.ReadFlag())
          throw new NotSupportedException(
            "This Indeo 5 band header carries extended transform information, which no clip observed "
            + "carries and whose layout is therefore unknown.");

        _SelectTransform(band, p, i);

        if (band.TransformSize != band.BlockSize)
          throw new InvalidDataException(
            $"Band {i} of plane {p} of this Indeo 5 clip states blocks of {band.BlockSize} samples, and "
            + $"the transform its position selects is {band.TransformSize} wide.");

        _SelectQuantisation(band, p, i, lumaBands);

        if (this.Reader.Read(2) != 0)
          throw new InvalidDataException(
            $"Band {i} of plane {p} of this Indeo 5 group header does not end with its two zero bits, so "
            + "the header was read out of step.");
      }
    }

    // The second chrominance plane is coded exactly like the first and states nothing of its own.
    for (var i = 0; i < chromaBands; ++i) {
      var from = this.Planes[1].Bands[i];
      var to = this.Planes[2].Bands[i];

      to.Width = from.Width;
      to.Height = from.Height;
      to.MacroblockSize = from.MacroblockSize;
      to.BlockSize = from.BlockSize;
      to.IsHalfPel = from.IsHalfPel;
      to.IntraBase = from.IntraBase;
      to.InterBase = from.InterBase;
      to.IntraScale = from.IntraScale;
      to.InterScale = from.InterScale;
      to.Scan = from.Scan;
      to.InverseTransform = from.InverseTransform;
      to.DcTransform = from.DcTransform;
      to.IsTwoDimensional = from.IsTwoDimensional;
      to.TransformSize = from.TransformSize;
    }

    if (layoutChanged)
      this.InitializeTiles(configuration.TileWidth, configuration.TileHeight);

    if ((flags & 8) != 0) {
      if (this.Reader.Read(3) != 0)
        throw new InvalidDataException(
          "This Indeo 5 group header states a transparency colour and the three bits before it are not "
          + "zero, so the header was read out of step.");

      if (this.Reader.ReadFlag())
        this.Reader.Skip(24); // The transparency fill colour.
    }

    this.Reader.Align();
    this.Reader.Skip(23);

    // A group extension, which is a chain of sixteen-bit words continuing while the top bit is set.
    if (this.Reader.ReadFlag())
      while ((this.Reader.Read(16) & 0x8000) != 0) {
      }

    this.Reader.Align();
  }

  /// <summary>
  /// Selects a band's transform and scan pattern from its position, which is the only thing that
  /// decides them.
  /// </summary>
  private static void _SelectTransform(IviBand band, int plane, int number) {
    switch ((plane << 2) + number) {
      case 0:
        band.InverseTransform = IviTransforms.InverseSlant8x8;
        band.DcTransform = IviTransforms.DcSlant2D;
        band.Scan = IviTables.ZigzagDirect;
        band.TransformSize = 8;
        band.IsTwoDimensional = true;
        break;
      case 1:
        band.InverseTransform = IviTransforms.RowSlant8;
        band.DcTransform = IviTransforms.DcRowSlant;
        band.Scan = IviTables.VerticalScan8x8;
        band.TransformSize = 8;
        band.IsTwoDimensional = false;
        break;
      case 2:
        band.InverseTransform = IviTransforms.ColumnSlant8;
        band.DcTransform = IviTransforms.DcColumnSlant;
        band.Scan = IviTables.HorizontalScan8x8;
        band.TransformSize = 8;
        band.IsTwoDimensional = false;
        break;
      case 3:
        band.InverseTransform = IviTransforms.PutPixels8x8;
        band.DcTransform = IviTransforms.PutDcPixel8x8;
        band.Scan = IviTables.HorizontalScan8x8;
        band.TransformSize = 8;
        band.IsTwoDimensional = false;
        break;
      case 4:
        band.InverseTransform = IviTransforms.InverseSlant4x4;
        band.DcTransform = IviTransforms.DcSlant2D;
        band.Scan = IviTables.DirectScan4x4;
        band.TransformSize = 4;
        band.IsTwoDimensional = true;
        break;
      default:
        throw new InvalidDataException(
          $"Band {number} of plane {plane} of this Indeo 5 clip is a position the format defines no "
          + "transform for.");
    }
  }

  /// <summary>
  /// Selects a band's dequantisation matrix, which its position decides as well.
  /// </summary>
  private static void _SelectQuantisation(IviBand band, int plane, int number, int lumaBands) {
    var matrix = plane == 0 ? lumaBands > 1 ? number + 1 : 0 : 5;

    if (band.BlockSize != 8) {
      band.IntraBase = Indeo5Tables.BaseQuant4x4Intra;
      band.InterBase = Indeo5Tables.BaseQuant4x4Inter;
      band.IntraScale = Indeo5Tables.ScaleQuant4x4Intra;
      band.InterScale = Indeo5Tables.ScaleQuant4x4Inter;
      return;
    }

    if (matrix >= 5)
      throw new InvalidDataException(
        $"Band {number} of plane {plane} of this Indeo 5 clip selects the sixth eight-by-eight "
        + "dequantisation matrix, and the format has five.");

    band.IntraBase = Indeo5Tables.BaseQuant8x8Intra[matrix];
    band.InterBase = Indeo5Tables.BaseQuant8x8Inter[matrix];
    band.IntraScale = Indeo5Tables.ScaleQuant8x8Intra[matrix];
    band.InterScale = Indeo5Tables.ScaleQuant8x8Inter[matrix];
  }

  // ============================================================================================
  // Band header
  // ============================================================================================

  protected override void DecodeBandHeader(IviBand band) {
    var flags = (int)this.Reader.Read(8);

    band.IsEmpty = (flags & 1) != 0;
    if (band.IsEmpty)
      return;

    if ((this._frameFlags & 0x80) != 0)
      this.Reader.Skip(24); // The band's data size, which its tiles account for themselves.

    band.InheritMotionVectors = (flags & 2) != 0;
    band.InheritQuantiserDelta = (flags & 8) != 0;
    band.QuantiserDeltaPresent = (flags & 4) != 0;
    if (!band.QuantiserDeltaPresent)
      band.InheritQuantiserDelta = true;

    band.CorrectionCount = 0;
    if ((flags & 0x10) != 0) {
      band.CorrectionCount = (int)this.Reader.Read(8);
      if (band.CorrectionCount > 61)
        throw new InvalidDataException(
          $"A band of this Indeo 5 frame states {band.CorrectionCount} run-value corrections, and the "
          + "format allows 61.");

      for (var i = 0; i < band.CorrectionCount * 2; ++i)
        band.Corrections[i] = (byte)this.Reader.Read(8);
    }

    // Which run-value map, selected by a flag already read rather than by a bit of its own — Indeo 4
    // spends a bit here and Indeo 5 does not.
    band.RunValueMapSelector = (flags & 0x40) != 0 ? (int)this.Reader.Read(3) : 8;

    this.ReadCodebook(band.Codebook, (flags & 0x80) != 0, isBlockCodebook: true);

    band.ChecksumPresent = this.Reader.ReadFlag();
    if (band.ChecksumPresent)
      band.Checksum = (int)this.Reader.Read(16);

    band.GlobalQuantiser = (int)this.Reader.Read(5);

    if ((flags & 0x20) != 0) {
      this.Reader.Align();
      this.SkipHeaderExtension();
    }

    this.Reader.Align();
  }

  // ============================================================================================
  // Macroblocks
  // ============================================================================================

  protected override void DecodeMacroblockInfo(IviBand band, IviTile tile) {
    var reference = tile.ReferenceMacroblocks;

    if (reference == null && ((band.QuantiserDeltaPresent && band.InheritQuantiserDelta) || band.InheritMotionVectors))
      throw new InvalidDataException(
        $"Band {band.BandNumber} of plane {band.Plane} of this Indeo 5 frame says its macroblocks inherit "
        + "from the first luminance band, and it is the first luminance band.");

    var expected = (tile.Width + band.MacroblockSize - 1) / band.MacroblockSize
                   * ((tile.Height + band.MacroblockSize - 1) / band.MacroblockSize);
    if (tile.Macroblocks.Length != expected)
      throw new InvalidDataException(
        $"A tile of band {band.BandNumber} of plane {band.Plane} of this Indeo 5 frame is "
        + $"{tile.Width}x{tile.Height} at macroblocks of {band.MacroblockSize}, which is {expected} "
        + $"macroblocks and not the {tile.Macroblocks.Length} it was built with.");

    var motionScale = (this.Planes[0].Bands[0].MacroblockSize >> 3) - (band.MacroblockSize >> 3);
    var blocksPerMacroblock = band.MacroblockSize != band.BlockSize ? 4 : 1;
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

        if (this.Reader.ReadFlag()) {
          if (this.FrameType == FrameTypeIntra)
            throw new InvalidDataException(
              "This Indeo 5 intra frame holds a macroblock that says it repeats the reference frame, and "
              + "an intra frame has no reference frame.");

          macroblock.Type = 1;
          macroblock.CodedBlockPattern = 0;
          macroblock.QuantiserDelta = 0;

          if (band.Plane == 0 && band.BandNumber == 0 && (this._frameFlags & 8) != 0)
            macroblock.QuantiserDelta = (sbyte)ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));

          macroblock.MotionX = 0;
          macroblock.MotionY = 0;

          if (band.InheritMotionVectors && source != null) {
            macroblock.MotionX = ScaleMotion(source.MotionX, motionScale);
            macroblock.MotionY = ScaleMotion(source.MotionY, motionScale);
          }
        } else {
          if (band.InheritMotionVectors && source != null)
            macroblock.Type = source.Type;
          else if (this.FrameType == FrameTypeIntra)
            macroblock.Type = 0;
          else
            macroblock.Type = (byte)this.Reader.ReadBit();

          macroblock.CodedBlockPattern = (byte)this.Reader.Read(blocksPerMacroblock);

          macroblock.QuantiserDelta = 0;
          if (band.QuantiserDeltaPresent) {
            if (band.InheritQuantiserDelta) {
              if (source != null)
                macroblock.QuantiserDelta = source.QuantiserDelta;
            } else if (macroblock.CodedBlockPattern != 0
                       || (band.Plane == 0 && band.BandNumber == 0 && (this._frameFlags & 8) != 0))
              macroblock.QuantiserDelta = (sbyte)ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));
          }

          if (macroblock.Type == 0) {
            macroblock.MotionX = 0;
            macroblock.MotionY = 0;
          } else if (band.InheritMotionVectors && source != null) {
            macroblock.MotionX = ScaleMotion(source.MotionX, motionScale);
            macroblock.MotionY = ScaleMotion(source.MotionY, motionScale);
          } else {
            motionY += ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));
            motionX += ToSigned(this.MacroblockCodebook.Table!.Read(this.Reader));
            macroblock.MotionX = (sbyte)motionX;
            macroblock.MotionY = (sbyte)motionY;
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
  // Buffers
  // ============================================================================================

  /// <summary>
  /// Rotates the band buffers, which for Indeo 5 depends on what the <i>previous</i> frame was as
  /// much as on what this one is.
  /// </summary>
  /// <remarks>
  /// A droppable predicted frame is not a reference, so the frame after it predicts from the same
  /// buffer it did rather than from what it produced. That is why the previous frame's type is
  /// consulted first and why the two switches are not one.
  /// </remarks>
  protected override void SwitchBuffers() {
    switch (this.PreviousFrameType) {
      case FrameTypeIntra:
      case FrameTypeInter:
        this._bufferSwitch ^= 1;
        this.DestinationBuffer = this._bufferSwitch;
        this.ReferenceBuffer = this._bufferSwitch ^ 1;
        break;
      case FrameTypeInterScalable:
        if (!this._scalableSequence) {
          this.ScalableReferenceBuffer = 2;
          this._scalableSequence = true;
        }

        (this.DestinationBuffer, this.ScalableReferenceBuffer) = (this.ScalableReferenceBuffer, this.DestinationBuffer);
        this.ReferenceBuffer = this.ScalableReferenceBuffer;
        break;
    }

    switch (this.FrameType) {
      case FrameTypeIntra:
        this._bufferSwitch = 0;
        goto case FrameTypeInter;
      case FrameTypeInter:
        this._scalableSequence = false;
        this.DestinationBuffer = this._bufferSwitch;
        this.ReferenceBuffer = this._bufferSwitch ^ 1;
        break;
    }
  }
}
