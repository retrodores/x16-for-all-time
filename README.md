# X16 For All Time

A Windows launcher for running a library of Commander X16 programs with your choice of X16Emu build.

## Start

Double-click an entry or select **Run** to launch it. The entry's saved configuration takes priority. **Start defaults** supplies an emulator and settings for entries without a configuration. The preview identifies the settings in use and **Copy PowerShell** copies the same command. The working directory is always the PRG's containing folder.

Use **Launch settings** to edit the selected PRG's name, path, and launch settings. **Duplicate entry** copies the entry so you can save another setup for the same PRG. **Remove from list** removes the entry and keeps the program file on disk.

Selecting an emulator fills the form from that build's default profile or emulator defaults. Select a different profile to prefill its values. Save a copy of the resulting configuration or choose **Use Start defaults** to let the entry follow the Start list's defaults. An entry whose emulator was removed requires reassignment.

Use **Add PRG** or **Edit PRG** to set the name, path, and launch settings in one dialog. **Duplicate entry** copies those fields into a new entry. **Remove from list** keeps the program file on disk. A relative GIF output path is resolved against the PRG’s folder. On Start, **Save GIF** overrides the recording filename for launches while checked. A blank filename uses demo.gif. The PRG’s saved settings stay intact.

## X16 Files

Use **Downloadable only** to show entries with a ZIP or PRG link in the catalog. The filter works with the search box.

Browse the X16 Files catalog, open a project page, or choose **Install to Start** to download a ZIP or PRG from the catalog and register its PRG in Start. **Reinstall to Start** updates the existing entry while preserving its launch settings. Use **Download and launch** to run a ZIP or PRG from the catalog immediately. The launcher keeps ZIP contents together and uses the catalog’s startup PRG when specified. With no startup hint, it uses the only PRG or asks you to choose among several. Catalog launches use Start defaults, or the default profile of the sole registered emulator, with **Run after loading** enabled.

## Emulators and profiles

Use **Register existing** for an unpacked folder or executable, including renamed builds. **Install or update** installs official Windows releases side by side. **Edit emulator** updates the build name, executable path, and whether standard X16Emu options are supported. ROM and extra arguments are set in the build’s profile. For the serial-network fork, use its compatible ROM and DLLs and enter -serialcard in the profile’s Extra arguments.

Each build starts with a **Default** profile and can have additional named profiles with **New profile**, **Edit**, **Duplicate**, **Delete**, and **Set as default**. Profiles prefill PRG settings and Start defaults. Saving either makes an independent copy, including the effective ROM and extra arguments. The standard controls show Commander X16 hardware choices. **CPU** offers 65C02 and 65C816. **Advanced hardware** contains custom clock and RAM values. Display scaling is available in the same editor.

**Remove from list** removes a registration and keeps its files on disk. PRGs referring to the build keep their options and ask for a new emulator. Profiles from removed builds remain available to copy.

## Build on Windows

Use a Visual Studio Developer PowerShell with the .NET Framework 4.8 targeting pack and Inno Setup 6. Run .\Build.ps1 to compile the application and create dist\X16ForAllTime-Setup.exe. Use -ISCC if Inno Setup is installed elsewhere. Windows PowerShell 5.1 handles emulator release discovery and installation.

## Installing and removing emulators

The installer shows progress while checking releases, downloading, verifying, extracting and installing. **Overwrite existing installation** permanently deletes the previous emulator folder after the replacement is installed and registered. If replacement or registration fails, the launcher attempts to restore the original folder. Existing registrations keep their profiles and PRG assignments. **Remove from list** leaves files on disk unless you choose **Also delete the emulator directory and its contents**. PRG entries and their settings can be edited before an emulator is available.
