using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Hpgl;

/// <summary>A Hewlett-Packard Graphics Language plot (.hpgl, .hgl, .plt).</summary>
/// <remarks>
/// The language a pen plotter was driven with: two letters, then parameters, then the next
/// instruction. <c>PU</c> lifts the pen and <c>PD</c> puts it down, and a list of coordinates after
/// either draws or travels to each in turn; <c>PA</c> and <c>PR</c> say whether those coordinates
/// are absolute or a step from where the pen is. Everything else sets how the pen behaves — which
/// one is in it, how wide it is, what pattern it draws — or draws one of the handful of shapes the
/// language has: a circle, an arc, a rectangle, a wedge, a polygon held in a buffer.
/// <para/>
/// A plotter unit is a fixed physical length, 0.025 of a millimetre in HP-GL/2, so the plot has a
/// real size and it is that size, taken at ninety-six pixels to the inch, that is drawn. Where the
/// file scales user units onto the plotting frame with <c>SC</c>, that frame is the pair of scaling
/// points <c>P1</c> and <c>P2</c>, and where the file does not move them they are the ones a
/// plotter powers up with — a frame of ten thousand by seven thousand two hundred units on A or A4
/// paper.
/// <para/>
/// Labels are not drawn. <c>LB</c> writes text with the plotter's own stick font, which is in the
/// plotter and not in the file, so the string is consumed and passed over rather than approximated.
/// <para/>
/// It does not write.
/// </remarks>
public readonly record struct HpglFile : IImageFormatReader<HpglFile>, IImageToRawImage<HpglFile> {

  /// <summary>How long one plotter unit is, in millimetres.</summary>
  public const double MillimetresPerUnit = 0.025;

  /// <summary>Where the scaling points sit on A or A4 paper before anything moves them.</summary>
  public const int DefaultP1X = 250, DefaultP1Y = 596, DefaultP2X = 10250, DefaultP2Y = 7796;

  /// <summary>The eight pens a plotter carousel holds, pen zero being no pen at all.</summary>
  public static readonly Rgba32[] Pens = [
    Rgba32.White,
    Rgba32.Black,
    new(255, 0, 0),
    new(0, 160, 0),
    new(255, 200, 0),
    new(0, 0, 255),
    new(255, 0, 255),
    new(0, 200, 255)
  ];

  static string IImageFormatMetadata<HpglFile>.PrimaryExtension => ".hpgl";
  // Not .plt, which plenty of plotter files do use but which PlotMaker already claims here. A
  // second format on the same name would decide by whichever the registry happened to try first.
  static string[] IImageFormatMetadata<HpglFile>.FileExtensions => [".hpgl", ".hgl", ".hpg"];
  static HpglFile IImageFormatReader<HpglFile>.FromSpan(ReadOnlySpan<byte> data) => HpglReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<HpglFile>.VideoModes => [
    new("Plot", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>The instructions the file holds, in order.</summary>
  public IReadOnlyList<HpglInstruction> Instructions { get; init; }

  public static RawImage ToRawImage(HpglFile file) => HpglRenderer.Render(file);
}

/// <summary>One instruction: its two-letter mnemonic and whatever followed it.</summary>
/// <param name="Mnemonic">The two letters, in upper case.</param>
/// <param name="Numbers">The numeric parameters.</param>
/// <param name="Text">The raw parameter text, which the instructions taking a string need.</param>
public readonly record struct HpglInstruction(string Mnemonic, double[] Numbers, string Text);
