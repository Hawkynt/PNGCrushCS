using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FileFormat.Core;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;

namespace Crush.Viewer;

/// <summary>Asks for a pixel command's settings and shows what they do before anything is changed.</summary>
/// <remarks>
/// <para>
/// One dialog for every command, built from the command's list of settings: a spinner per number, a
/// drop-down per choice. What differs between commands — which operation, what it asks for, what the
/// preview should cover — is data in <see cref="PixelCommands"/>, so a command that grows a setting
/// grows its dialog without anybody writing one.
/// </para>
/// <para>
/// The preview never runs on the picture itself. It runs on a stand-in the caller prepares — the
/// whole picture shrunk to the preview box, or the middle of it at full size, or the area around a
/// marked eye — which is at most a few hundred pixels each way, so even the slowest effect answers a
/// change in a fraction of a second. It runs off the interface thread all the same, a short pause
/// after the last change so that holding a spinner's arrow down does not queue up a render per step,
/// and a render that has been overtaken by a newer one is thrown away rather than shown.
/// </para>
/// </remarks>
internal sealed class PixelCommandDialog : DialogForm {

  /// <summary>The preview box, in client pixels; the stand-in is prepared to fit it.</summary>
  internal const int PREVIEW_WIDTH = ClientWidth - 28;

  /// <inheritdoc cref="PREVIEW_WIDTH"/>
  internal const int PREVIEW_HEIGHT = 260;

  private const int _SETTLE_MS = 120;

  private readonly IPlatformBackend _backend;
  private readonly RawImage _stand;
  private readonly string _standDescription;
  private readonly Func<RawImage, double[], RawImage> _run;
  private readonly List<Func<double>> _readers = [];
  private readonly PictureBox _preview;
  private readonly Label _status;
  private readonly Timer _settle = new() { Interval = _SETTLE_MS };
  private IImage? _shown;
  private int _generation;

  /// <param name="backend">The platform the preview images are made on.</param>
  /// <param name="title">The window's title.</param>
  /// <param name="note">A sentence above the settings saying what the command does.</param>
  /// <param name="settings">What to ask for, in the order the operation takes it.</param>
  /// <param name="stand">The stand-in the preview runs on.</param>
  /// <param name="standDescription">What the stand-in is, for the line under the preview.</param>
  /// <param name="run">The operation, given the stand-in and the current values.</param>
  /// <param name="acceptText">What the accept button says.</param>
  internal PixelCommandDialog(
    IPlatformBackend backend,
    string title,
    string note,
    IReadOnlyList<PixelSetting> settings,
    RawImage stand,
    string standDescription,
    Func<RawImage, double[], RawImage> run,
    string acceptText = "Apply") : base(title) {
    this._backend = backend;
    this._stand = stand;
    this._standDescription = standDescription;
    this._run = run;

    this.AddParagraph(note);
    foreach (var setting in settings)
      this._readers.Add(this._AddSetting(setting));

    this._preview = this.AddWide(new PictureBox { SizeMode = PictureBoxSizeMode.Zoom }, PREVIEW_HEIGHT);
    this._status = this.AddNote(standDescription, 22);
    this.AddButtons(acceptText);

    this._settle.Tick += (_, _) => {
      this._settle.Stop();
      this._Render();
    };
    this.Load += (_, _) => this._Render();
    this.FormClosed += (_, _) => {
      this._settle.Stop();
      Interlocked.Increment(ref this._generation);
      this._preview.Image = null;
      this._shown?.Dispose();
      this._shown = null;
    };
  }

  /// <summary>The values as they stand, one per setting, in order.</summary>
  internal double[] Values => this._readers.ConvertAll(read => read()).ToArray();

  private Func<double> _AddSetting(PixelSetting setting) {
    if (setting.Choices is { } choices) {
      var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
      foreach (var choice in choices)
        box.Items.Add(choice);
      box.SelectedIndex = Math.Clamp((int)setting.Initial, 0, choices.Length - 1);
      box.SelectedIndexChanged += (_, _) => this._Changed();
      this.AddRow(setting.Label, box);
      return () => Math.Max(0, box.SelectedIndex);
    }

    var step = (decimal)Math.Pow(10, -setting.Decimals);
    var spinner = new NumericUpDown {
      Minimum = (decimal)setting.Minimum,
      Maximum = (decimal)setting.Maximum,
      DecimalPlaces = setting.Decimals,
      Increment = setting.Decimals == 0 ? 1 : step * 10,
      Value = Math.Clamp((decimal)setting.Initial, (decimal)setting.Minimum, (decimal)setting.Maximum),
    };
    spinner.ValueChanged += (_, _) => this._Changed();
    this.AddRow(setting.Label, spinner);
    return () => (double)spinner.Value;
  }

  private void _Changed() {
    this._settle.Stop();
    this._settle.Start();
  }

  private void _Render() {
    var generation = Interlocked.Increment(ref this._generation);
    var values = this.Values;
    this._status.Text = "Working…";

    Task.Run(() => {
      var clock = Stopwatch.StartNew();
      try {
        var result = this._run(this._stand, values);
        var elapsed = clock.Elapsed;
        var pixels = ImageBridge.ToArgb(result);
        this.BeginInvoke(() => this._Show(generation, result.Width, result.Height, pixels, elapsed, null));
      } catch (Exception ex) {
        this.BeginInvoke(() => this._Show(generation, 0, 0, null, clock.Elapsed, ex.Message));
      }
    });
  }

  private void _Show(int generation, int width, int height, int[]? pixels, TimeSpan elapsed, string? failure) {
    if (generation != Volatile.Read(ref this._generation))
      return;

    if (failure != null || pixels == null || width < 1 || height < 1) {
      this._status.Text = $"The preview failed: {failure ?? "the operation returned no pixels"}";
      return;
    }

    var previous = this._shown;
    this._shown = this._backend.CreateImage(width, height, pixels);
    this._preview.Image = this._shown;
    previous?.Dispose();
    this._status.Text = $"{this._standDescription} {elapsed.TotalMilliseconds:0} ms.";
  }
}
