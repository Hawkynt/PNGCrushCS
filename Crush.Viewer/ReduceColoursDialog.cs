using System;
using System.Linq;
using System.Text;
using Hawkynt.ColorProcessing.Adapter;
using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>Asks which quantizer, which ditherer and how many colours to reduce a picture to.</summary>
/// <remarks>
/// <para>
/// The two lists are <see cref="ColorReductionDispatch.QuantizerNames"/> and
/// <see cref="ColorReductionDispatch.DithererNames"/>, which a source generator writes out of
/// whatever colour library the build referenced. So this dialog offers exactly what the build can
/// actually run: nothing is hard-coded here, and a library that grows a quantizer grows this dialog
/// with it.
/// </para>
/// <para>
/// The generated names are type names — <c>WuQuantizer</c>, <c>ErrorDiffusion.FloydSteinberg</c> —
/// because that is what can be checked at build time. They are shown split into words and are still
/// the strings handed back, so what a user picked here is the same name the command line takes.
/// </para>
/// </remarks>
internal sealed class ReduceColoursDialog : DialogForm {

  /// <summary>What the old single-number dialog used, and still the pair worth defaulting to.</summary>
  private const string _DEFAULT_QUANTIZER = "Median Cut";
  private const string _DEFAULT_DITHERER = "ErrorDiffusion_FloydSteinberg";

  private readonly ComboBox _quantizer;
  private readonly ComboBox _ditherer;
  private readonly NumericUpDown _colours;

  internal ReduceColoursDialog(string description, int colours) : base("Reduce colours") {
    this.AddNote(description, 36);

    this._quantizer = this.AddRow("Palette chosen by", _Names(
      ColorReductionDispatch.QuantizerNames, ColorReductionDispatch.ResolveQuantizer(_DEFAULT_QUANTIZER)));
    this._ditherer = this.AddRow("Pixels placed by", _Names(
      ColorReductionDispatch.DithererNames, ColorReductionDispatch.ResolveDitherer(_DEFAULT_DITHERER)));
    this._colours = this.AddRow("Colours", new NumericUpDown {
      Minimum = 2,
      Maximum = 256,
      Increment = 1,
      Value = Math.Clamp(colours, 2, 256),
    });

    this.AddButtons("Reduce");
  }

  /// <summary>The generated name of the quantizer the user picked.</summary>
  internal string Quantizer => (string)this._quantizer.SelectedItem!;

  /// <summary>The generated name of the ditherer the user picked.</summary>
  internal string Ditherer => (string)this._ditherer.SelectedItem!;

  /// <summary>How many colours the palette is to hold.</summary>
  internal int Colours => (int)this._colours.Value;

  /// <summary>
  /// A drop-down of generated names, sorted by what they read as and opened on the default.
  /// </summary>
  /// <remarks>
  /// The default arrives already resolved through the dispatch's own resolver, so the spelling this
  /// dialog asks for is the spelling the rest of the application uses. A name the resolver does not
  /// know — a library built without that quantizer — leaves the first entry selected rather than
  /// nothing, because a drop-down list with no selection has no value to give.
  /// </remarks>
  private static ComboBox _Names(string[] names, string? preferred) {
    var sorted = names.OrderBy(_Words, StringComparer.CurrentCultureIgnoreCase).ToArray();
    var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplaySelector = o => _Words((string)o!) };
    foreach (var name in sorted)
      box.Items.Add(name);

    box.SelectedIndex = Math.Max(0, preferred == null ? -1 : Array.IndexOf(sorted, preferred));
    return box;
  }

  /// <summary>Breaks a type name into words, so a list of them reads as a list of choices.</summary>
  /// <remarks>
  /// A space goes in front of a capital that follows a lower-case letter or a digit, which splits
  /// <c>FloydSteinberg</c> and leaves <c>Bayer4x4</c> and an acronym alone. The dot a generated name
  /// uses to join a type to its configuration becomes a middle dot, because the two halves are not a
  /// sentence and a hyphen would read as part of a name.
  /// </remarks>
  private static string _Words(string name) {
    var text = new StringBuilder(name.Length + 8);
    foreach (var (character, index) in name.Select((c, i) => (c, i))) {
      if (character == '.') {
        text.Append(" · ");
        continue;
      }

      if (index > 0 && char.IsUpper(character) && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])))
        text.Append(' ');

      text.Append(character);
    }

    return text.ToString();
  }
}
