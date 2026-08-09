using System;
using System.Globalization;
using System.Text;

namespace FileFormat.Vrml;

/// <summary>Writes a picture as a VRML 2.0 scene carrying it in an inline <c>PixelTexture</c>.</summary>
/// <remarks>
/// The scene is the one XnView's converter emits and nothing more: a unit box, a white material, and
/// the picture as the box's texture. There is no point inventing a richer scene — nothing reads a
/// <c>.wrl</c> back as a picture except a reader looking for exactly this node, so the value of the
/// file is in the texture and the geometry is only somewhere to hang it.
/// <para/>
/// Byte for byte the same as that converter's output, which is how it was checked: the same picture
/// handed to both comes out the same file, at 6x4, 5x3, 10x2, 13x2, 7x1, 1x7 and 4x2. That pins three
/// things a looser writer would get wrong — the pixels are four to a line counted from the start of
/// each ROW rather than of the field, so the wrapping follows the width; the digits are lowercase and
/// padded to two a component; and the first row written is the BOTTOM row of the picture, a texture's
/// origin being its lower-left corner.
/// <para/>
/// The lines are joined here rather than written as one literal because the join has to be a bare
/// newline whatever the checkout does to the source file's own line endings, and a stray carriage
/// return would be invisible in every test that compares pictures rather than bytes.
/// </remarks>
public static class VrmlWriter {

  /// <summary>Pixels a line of the field holds, counted from the start of a row.</summary>
  private const int _PER_LINE = 4;

  /// <summary>Everything ahead of the picture's own numbers, the last line ending at <c>image</c>.</summary>
  private static readonly string[] _Prologue = [
    "#VRML V2.0 utf8",
    "Group {",
    "  children [",
    "    Shape {",
    "      appearance Appearance {",
    "        material Material {",
    "          diffuseColor 1.0 1.0 1.0",
    "        }",
    "        texture PixelTexture {",
  ];

  /// <summary>Everything behind the last pixel, closing the texture and the scene around it.</summary>
  private static readonly string[] _Epilogue = [
    "        }",
    "      }",
    "      geometry Box {}",
    "    }",
    "  ]",
    "}",
  ];

  public static byte[] ToBytes(VrmlFile file) {
    var width = file.Width;
    var height = file.Height;
    var components = file.Components;
    var pixels = file.PixelData ?? [];
    var digits = "x" + (components * 2).ToString(CultureInfo.InvariantCulture);

    var text = new StringBuilder();
    foreach (var line in _Prologue)
      text.Append(line).Append('\n');

    text.Append("          image ")
      .Append(width.ToString(CultureInfo.InvariantCulture)).Append(' ')
      .Append(height.ToString(CultureInfo.InvariantCulture)).Append(' ')
      .Append(components.ToString(CultureInfo.InvariantCulture)).Append('\n');

    // Bottom row first, and each component in turn from the most significant byte down.
    for (var row = 0; row < height; ++row) {
      var from = (height - 1 - row) * width * components;
      for (var x = 0; x < width; ++x) {
        var value = 0u;
        for (var c = 0; c < components; ++c) {
          var at = from + x * components + c;
          value = (value << 8) | (at < pixels.Length ? pixels[at] : 0u);
        }

        text.Append("0x").Append(value.ToString(digits, CultureInfo.InvariantCulture)).Append(' ');
        if (x % _PER_LINE == _PER_LINE - 1)
          text.Append('\n');
      }
    }

    foreach (var line in _Epilogue)
      text.Append(line).Append('\n');

    return Encoding.ASCII.GetBytes(text.ToString());
  }
}
