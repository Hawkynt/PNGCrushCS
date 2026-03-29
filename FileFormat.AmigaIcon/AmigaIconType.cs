namespace FileFormat.AmigaIcon;

/// <summary>The type of an Amiga Workbench icon, stored at byte 54 of the DiskObject header.</summary>
public enum AmigaIconType : byte {
  Disk = 1,
  Drawer = 2,
  Tool = 3,
  Project = 4,
  Garbage = 5,
  Device = 6,
  Kick = 7,
  AppIcon = 8,
}
