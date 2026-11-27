using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>Asks which container and codec a video should be written as, and where.</summary>
/// <remarks>
/// The first codec offered is "keep it", which copies the coded packets across unchanged — the
/// lossless choice whenever only the container needs to change. Any other codec re-encodes the video.
/// </remarks>
internal sealed class VideoConvertDialog : DialogForm {

  private const string _KEEP = "Keep the coded video (remux, lossless)";

  private readonly ComboBox _container;
  private readonly ComboBox _codec;
  private readonly TextBox _destination;
  private readonly CheckBox _overwrite;

  internal VideoConvertDialog(VideoDocument video, string destination) : base("Convert this video") {
    this.AddNote($"{video.File.Name}: {video.Container.Name}, {video.CodecName}, {video.Video.Width} x {video.Video.Height}.");

    var containers = VideoService.WritableContainers;
    this._container = this.AddRow("Container", new ComboBox {
      DropDownStyle = ComboBoxStyle.DropDownList,
      DisplaySelector = static o => o is VideoFormatEntry entry ? $"{entry.Name} ({entry.PrimaryExtension})" : "",
    });
    foreach (var entry in containers)
      this._container.Items.Add(entry);
    this._container.SelectedIndex = Math.Max(0, containers.ToList().FindIndex(e => e.Format == video.Container.Format));

    this._codec = this.AddRow("Video codec", new ComboBox {
      DropDownStyle = ComboBoxStyle.DropDownList,
      DisplaySelector = static o => o switch {
        VideoCodecEncoderEntry entry => $"{entry.CodecName} ({entry.Codec})",
        string text => text,
        _ => "",
      },
    });
    this._codec.Items.Add(_KEEP);
    foreach (var encoder in VideoService.Encoders)
      this._codec.Items.Add(encoder);
    this._codec.SelectedIndex = 0;

    this._destination = this.AddRow("Into folder", new TextBox { Text = destination });
    var browse = this.AddWide(new Button { Text = "Choose folder…" });
    browse.Click += (_, _) => {
      var picker = new FolderBrowserDialog { Title = "Destination folder", SelectedPath = this._destination.Text };
      if (picker.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(picker.SelectedPath))
        this._destination.Text = picker.SelectedPath;
    };

    this._overwrite = this.AddWide(new CheckBox { Text = "Replace a file that is already there" });
    this.AddButtons("Convert");
  }

  /// <summary>The container picked.</summary>
  internal VideoFormatEntry Container => (VideoFormatEntry)this._container.SelectedItem!;

  /// <summary>The codec to re-encode as, or <c>null</c> to keep the coded video.</summary>
  internal CodecTag? Codec => this._codec.SelectedItem is VideoCodecEncoderEntry entry ? entry.Codec : null;

  /// <summary>The folder the result goes into.</summary>
  internal DirectoryInfo Destination => new(this._destination.Text.Trim());

  /// <summary>Whether an existing file of the same name may be replaced.</summary>
  internal bool Overwrite => this._overwrite.Checked;

  protected override bool Validate(out string error) {
    if (this._container.SelectedItem is not VideoFormatEntry) {
      error = "Pick a container to write.";
      return false;
    }

    var path = this._destination.Text.Trim();
    if (path.Length == 0) {
      error = "Name a folder for the result.";
      return false;
    }

    try {
      _ = Path.GetFullPath(path);
    } catch (Exception ex) {
      error = $"That is not a usable path: {ex.Message}";
      return false;
    }

    error = "";
    return true;
  }
}
