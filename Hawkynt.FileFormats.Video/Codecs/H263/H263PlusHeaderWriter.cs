using System;

namespace FileFormat.Codecs.H263;

/// <summary>Writes the H.263+ picture-header subset used by custom-size I/P pictures and Annex O B-pictures.</summary>
internal static class H263PlusHeaderWriter {

  private const int _PICTURE_START_CODE = 1 << 5;
  private const int _PICTURE_START_CODE_LENGTH = 22;

  /// <summary>
  /// Writes a full extended PTYPE (UFEP=001), with every optional coding mode disabled.
  /// </summary>
  /// <remarks>
  /// A full update is deliberately used for every picture this writer owns. H.263 permits the optional
  /// part of PLUSPTYPE to be omitted after a previous update, but carrying it every time makes each
  /// packet independently parsable and avoids hidden mode state crossing packet boundaries. B-pictures
  /// are placed in enhancement layer 2 and reference base layer 1, which is the temporal-scalability
  /// arrangement of Annex O for I/P anchors.
  /// </remarks>
  internal static void Write(
    H263BitWriter writer,
    int width,
    int height,
    int sourceFormat,
    int temporalReference,
    int quantiser,
    H263PictureKind pictureKind,
    int roundingType = 0) {
    ArgumentNullException.ThrowIfNull(writer);

    if (sourceFormat is < 1 or > 6)
      throw new ArgumentOutOfRangeException(nameof(sourceFormat));
    if (quantiser is < 1 or > 31)
      throw new ArgumentOutOfRangeException(nameof(quantiser));
    if (roundingType is < 0 or > 1)
      throw new ArgumentOutOfRangeException(nameof(roundingType));
    if (pictureKind is not (H263PictureKind.Intra or H263PictureKind.Predicted or H263PictureKind.Bidirectional))
      throw new NotSupportedException($"The H.263+ writer does not encode picture type {pictureKind}.");
    if (roundingType != 0 && pictureKind != H263PictureKind.Predicted)
      throw new ArgumentException("RTYPE may be one only for a P-picture in the subset this writer emits.", nameof(roundingType));

    if (sourceFormat == 6) {
      if (width is < 4 or > 2048 || height is < 4 or > 1152 || (width & 3) != 0 || (height & 3) != 0)
        throw new ArgumentOutOfRangeException(nameof(width),
          $"An H.263 custom source format must be 4..2048 by 4..1152 pixels and both dimensions multiples of four; {width}x{height} was supplied.");
    }

    writer.Write(_PICTURE_START_CODE, _PICTURE_START_CODE_LENGTH);
    writer.Write(temporalReference & 0xFF, 8);

    // Baseline PTYPE prefix: fixed H.263 discriminator, display flags clear, source format 111 to
    // announce PLUSPTYPE. Nothing after source format belongs to baseline PTYPE in this form.
    writer.WriteBit(1);
    writer.WriteBit(0);
    writer.Write(0, 3);
    writer.Write(7, 3);

    // UFEP = 001: carry the complete optional part every time.
    writer.Write(1, 3);

    // OPPTYPE (18 bits): source format, CIF clock, every optional coding mode off, marker, reserved.
    writer.Write(sourceFormat, 3);
    writer.WriteBit(0); // custom PCF
    writer.Write(0, 10); // UMV, SAC, AP, AIC, DF, SS, RPS, ISD, AIV, MQ
    writer.WriteBit(1); // marker
    writer.Write(0, 3); // reserved

    // MPPTYPE (9 bits): type, RPR/RRU off, RTYPE, reserved, marker.
    writer.Write((int)pictureKind, 3);
    writer.Write(0, 2);
    writer.WriteBit(roundingType);
    writer.Write(0, 2);
    writer.WriteBit(1);

    // With PLUSPTYPE present CPM moves immediately after it.
    writer.WriteBit(0);

    if (sourceFormat == 6) {
      // CPFMT: square pixels, width in groups of four minus one, anti-emulation marker, height/4.
      writer.Write(1, 4);
      writer.Write(width / 4 - 1, 9);
      writer.WriteBit(1);
      writer.Write(height / 4, 9);
    }

    if (pictureKind == H263PictureKind.Bidirectional) {
      writer.Write(2, 4); // ELNUM: first enhancement layer
      writer.Write(1, 4); // RLNUM: base-layer I/P anchors
    }

    writer.Write(quantiser, 5);
    writer.WriteBit(0); // PEI
  }
}
