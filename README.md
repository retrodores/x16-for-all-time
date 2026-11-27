# X16 For All Time

A Windows launcher for running a library of Commander X16 programs with your choice of X16Emu build.

[Download the Windows installer](https://github.com/retrodores/x16-for-all-time/releases/latest)

## Start

Drag a program above or below another program to reorder Start. The right-click menu also offers **Move up**, **Move down**, **Send to top**, and **Send to bottom**. F.A.T. saves the order across restarts. Start names are unique. Add a version or another detail when renaming or adding a program with a name already in use.

Choose **Browse X16 Files** to find a program or **Add a local program** to use one you have. On first run, the launcher opens **Emulators** and prompts you to install X16Emu or register an existing copy. Registering the first emulator unlocks **Start**, **X16 Files**, and **SD Card Kit**.

**Run once…** opens a PRG, BAS, or ZIP without adding a Start entry. The launch dialog starts with Start defaults and lets you pick another emulator, prefill from a profile, or adjust settings for that session. Loose programs run in their original folder. ZIPs keep their companion files for the emulator session and are cleaned up when it exits.

Double-click an entry or select **Run** to launch it. The entry's saved configuration takes priority. **Start defaults** supplies an emulator and settings for entries without a configuration. The preview identifies the settings in use and **Copy PowerShell** copies the same command. The working directory is always the PRG's containing folder.

Use **Launch settings** or right-click and choose **Edit** to change the selected program’s name, path, and launch settings. **Duplicate entry** copies the entry so you can save another setup for the same PRG. **Remove from Start** in the program menu removes only the listing. **Delete files** removes the selected entries and deletes their PRG or BAS files after confirmation. Use Shift to select a range or Ctrl to select individual rows. Select every entry that shares a file before deleting it.

Selecting an emulator fills the form from that build's default profile or emulator defaults. Select a different profile to prefill its values. Save a copy of the resulting configuration or choose **Use Start defaults** to let the entry follow the Start list's defaults. If there is no working program assignment, the launcher checks **Start defaults**. If those are unavailable or unset, it uses the default profile of the earliest registered emulator whose executable still exists.

Use **Add a local program** or **Launch settings** to set the name, path, and launch settings in one dialog. Browse for a PRG, a plain-text BASIC program, or a ZIP. The launcher keeps ZIP contents together and asks which PRG or BAS to use when there are several. **Duplicate entry** copies those fields into a new entry. Companion files and folders stay on disk when you delete a program. A relative GIF output path is resolved against the PRG’s folder. On Start, **Save GIF** overrides the recording filename for launches while checked. Use **Browse** to choose the destination. The picker opens on the Desktop when the field is blank. Each Start entry remembers its own destination. Leaving the field blank uses a filename based on the entry ID in the program’s folder. The PRG’s saved settings stay intact.

## X16 Files

Expand a category to see its programs. Use **Downloadable only** to show entries with a ZIP or PRG link in the catalog. Use **Clear** to reset the search while keeping that filter active, or **Expand all** and **Collapse all** to change how much of the current tree is open.

Browse the X16 Files catalog, open a project page, or choose **Install to Start** to download a ZIP or PRG from the catalog and register its PRG in Start. **Reinstall to Start** updates the existing entry while preserving its launch settings. The Start program menu offers **Open previous folder** and **Revert to previous install**, so you can recover saves from the previous folder. **Program information** shows available source and creator details. Use **Download and run once** to run a ZIP or PRG from the catalog immediately. The launcher keeps ZIP contents together and uses the catalog’s startup PRG when specified. With no startup hint, it uses the only PRG or asks you to choose among several. Catalog launches use the same emulator selection as Start, with **Run after loading** enabled. A one-off launch leaves Start entries and their settings as saved. This also applies to SD Card Kit programs.

## SD Card Kit

Browse the Commander X16 SD Card Starter Kit as a folder tree. Search by program or folder path and use **Clear** to reset the search. **Expand all** and **Collapse all** include nested folders. Use **Refresh from SD Card Kit** to fetch the current listing. The tree shows PRGs, BAS files, ZIP packages, and the folders that contain them. Use **Install to Start**, **Reinstall to Start**, or **Download and run once**. Double-click a program to install or reinstall it. The first import downloads the kit for that revision so that programs can access companion files. Later imports reuse the local copy. ZIPs offer a startup picker when they contain multiple PRG or BAS files.

## Emulators and profiles

Use **Register existing** for an unpacked folder or executable, including renamed builds. **Install or update** installs official Windows releases side by side. **Edit emulator** updates the build name, executable path, and whether standard X16Emu options are supported. ROM and extra arguments are set in the build’s profile. For serial networking, select **R49 Win64** and check **Install Serial/Network overlay**. F.A.T. installs the verified overlay ZIP into the R49 folder and keeps the base ROM. **Enable serial card** starts checked for this build and can be turned off per profile or program. The control is disabled for stock emulators. The matching manually overlaid R49 installation can also be registered.

Each build starts with a **Default** profile and can have additional named profiles with **New profile**, **Edit**, **Duplicate**, **Delete**, and **Set as default**. Profiles prefill PRG settings and Start defaults. Saving either makes an independent copy, including the effective ROM and extra arguments. Each X16Emu install creates 24 presets that cover 65C02 and 65C816, 2, 4, and 8 MHz, and 512, 1024, 1536, and 2048 KiB of banked RAM. Every new preset and the new **Default** profile use 2× display scale. Reinstalling preserves edits to those profiles. Use **Advanced hardware** to enter custom clock and RAM values.

**Open folder** opens the selected emulator’s directory in File Explorer. **Remove emulator** offers **Remove from list** to keep the folder on disk or **Delete emulator files** to permanently delete the folder after confirmation. PRGs referring to the build keep their saved options and use the Start fallback when available. The launch settings picker shows each distinct settings combination once, with profiles from removed builds listed after the current emulator’s profiles.

## Linux and macOS ports

The port specification describes the launcher, X16 Files JSON feed, ZIP handling, and X16Emu command line. [SPECIFICATION.md](SPECIFICATION.md)

## Build on Windows

Use a Visual Studio Developer PowerShell with the .NET Framework 4.8 targeting pack and Inno Setup 6. Run .\Build.ps1 to compile the application and create dist\X16ForAllTime-Setup.exe. Use -ISCC if Inno Setup is installed elsewhere. Windows PowerShell 5.1 handles emulator release discovery and installation.

## Installing and removing emulators

The installer shows progress while checking releases, downloading, verifying, extracting and installing. **Overwrite existing installation** permanently deletes the previous emulator folder after the replacement is installed and registered. If replacement or registration fails, the launcher attempts to restore the original folder. Existing registrations keep their profiles and PRG assignments. PRG entries and their settings can be edited before an emulator is available.

## Contributing

AI agent-led contributions are preferred. Read the project guidance before starting work. [CONTRIBUTORS.md](CONTRIBUTORS.md)
