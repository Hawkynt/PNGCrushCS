using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Cgm;

/// <summary>Serialises a metafile's commands in the binary encoding.</summary>
/// <remarks>
/// Each command is one word — four bits of class, seven of identifier, five of parameter length —
/// and then the parameters, padded to a word boundary. A list too long for five bits sets the length
/// to thirty-one and states the real one in partitions, each a word of a continuation bit and a
/// fifteen-bit count.
/// <para/>
/// Every partition written here is an even number of bytes. The standard pads an odd one and the
/// pad is not counted, which is exactly the sort of arithmetic that comes apart when a cell array's
/// rows are already being padded inside the list; keeping partitions even means the two paddings
/// never have to be reasoned about at once.
/// </remarks>
public static class CgmWriter {

  /// <summary>The length that says the real one follows in partitions.</summary>
  private const int _LongFormEscape = 31;

  /// <summary>How many bytes one partition may carry, kept even.</summary>
  private const int _PartitionSize = 32766;

  public static byte[] ToBytes(CgmFile file) {
    var commands = file.Commands ?? throw new ArgumentException("A metafile with no commands cannot be written.", nameof(file));

    using var output = new MemoryStream();
    foreach (var command in commands)
      _Write(output, command);

    return output.ToArray();
  }

  private static void _Write(Stream output, CgmCommand command) {
    var parameters = command.Parameters ?? [];
    var header = (command.ElementClass << 12) | (command.ElementId << 5);

    if (parameters.Length < _LongFormEscape) {
      _Word(output, header | parameters.Length);
      output.Write(parameters);
      if ((parameters.Length & 1) != 0)
        output.WriteByte(0);

      return;
    }

    _Word(output, header | _LongFormEscape);

    var at = 0;
    do {
      var partition = Math.Min(_PartitionSize, parameters.Length - at);
      var more = at + partition < parameters.Length;
      _Word(output, (more ? 0x8000 : 0) | partition);
      output.Write(parameters, at, partition);
      if ((partition & 1) != 0)
        output.WriteByte(0);

      at += partition;
    } while (at < parameters.Length);
  }

  private static void _Word(Stream output, int value) {
    output.WriteByte((byte)(value >> 8));
    output.WriteByte((byte)value);
  }

  /// <summary>The commands of a metafile holding one picture as a cell array.</summary>
  /// <remarks>
  /// A cell array is the standard's own way of putting a raster into a metafile, and it is what the
  /// picture goes in as — not traced into paths, which would put geometry into the file that the
  /// picture never had.
  /// <para/>
  /// The picture's own pixel grid is the picture's extent, one cell to the pixel, and the array's
  /// corners are chosen so that the first cell stored is the top-left one: <c>P</c> at the top left,
  /// <c>R</c> at the top right so a row runs left to right, and <c>Q</c> at the bottom right so the
  /// rows advance downward. The metafile's y axis points up, so the top of the picture is the larger
  /// y.
  /// </remarks>
  public static IReadOnlyList<CgmCommand> Picture(RawImage image, string name) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentOutOfRangeException(nameof(image), $"A metafile picture of {image.Width} by {image.Height} has nothing in it.");

    // A coordinate is a signed sixteen-bit integer at the default precision, so a picture wider than
    // that has no extent the file could state.
    if (image.Width > short.MaxValue || image.Height > short.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(image),
        $"A metafile states its extent in signed sixteen-bit coordinates, and {image.Width} by {image.Height} does not fit in them.");

    var width = image.Width;
    var height = image.Height;
    var rgb = image.ToRgb24();

    return [
      new(0, 1, _String(name)),
      new(1, 1, _Integer16(1)),
      new(1, 3, _Integer16(0)),
      new(1, 4, _Integer16(16)),
      new(1, 7, _Integer16(8)),
      // Which elements the file uses: a count of the pairs and then the pairs. Class -1 and
      // identifier 1 is the standard's shorthand for the drawing-plus-control set, which is what a
      // metafile of one cell array draws from.
      new(1, 11, _Integer16(1, -1, 1)),
      new(0, 3, _String(name)),
      new(2, 1, _ScalingMode()),
      new(2, 2, _Integer16(1)),
      new(2, 6, _Integer16(0, 0, (short)width, (short)height)),
      new(2, 7, [255, 255, 255]),
      new(0, 4, []),
      new(4, 9, _CellArray(rgb, width, height)),
      new(0, 5, []),
      new(0, 2, []),
    ];
  }

  /// <summary>Abstract scaling, which is what makes one coordinate one cell and one cell one pixel.</summary>
  private static byte[] _ScalingMode() {
    var parameters = new byte[6];
    // The mode is an enumeration and the metric factor a real, which goes unused when the mode is
    // abstract but is part of the element all the same.
    parameters[0] = 0;
    parameters[1] = 0;
    return parameters;
  }

  private static byte[] _CellArray(byte[] rgb, int width, int height) {
    var rowBytes = width * 3;
    var padded = rowBytes + (rowBytes & 1);
    var parameters = new byte[6 * 2 + 3 * 2 + padded * height];

    var at = 0;
    void Point(int x, int y) {
      parameters[at++] = (byte)(x >> 8);
      parameters[at++] = (byte)x;
      parameters[at++] = (byte)(y >> 8);
      parameters[at++] = (byte)y;
    }

    void Integer(int value) {
      parameters[at++] = (byte)(value >> 8);
      parameters[at++] = (byte)value;
    }

    Point(0, height);          // P: the first cell, at the top left
    Point(width, 0);           // Q: diagonally opposite, at the bottom right
    Point(width, height);      // R: the third corner, so a row runs left to right
    Integer(width);
    Integer(height);
    Integer(0);                // The colours are at the file's own precision.

    for (var y = 0; y < height; ++y) {
      Array.Copy(rgb, y * rowBytes, parameters, at, rowBytes);
      at += padded;
    }

    return parameters;
  }

  private static byte[] _Integer16(params short[] values) {
    var parameters = new byte[values.Length * 2];
    for (var i = 0; i < values.Length; ++i) {
      parameters[i * 2] = (byte)(values[i] >> 8);
      parameters[i * 2 + 1] = (byte)values[i];
    }

    return parameters;
  }

  private static byte[] _String(string value) {
    var text = Encoding.Latin1.GetBytes(value ?? string.Empty);
    if (text.Length > 254)
      text = text[..254];

    var parameters = new byte[1 + text.Length];
    parameters[0] = (byte)text.Length;
    text.CopyTo(parameters, 1);
    return parameters;
  }
}
