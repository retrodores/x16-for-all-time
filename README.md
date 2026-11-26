# X16 For All Time

A Windows launcher for running a library of Commander X16 programs with your choice of X16Emu build.

## Start

Drag a program above or below another program to reorder Start. The right-click menu also offers **Move up**, **Move down**, **Send to top**, and **Send to bottom**. F.A.T. saves the order across restarts. Start names are unique. Add a version or another detail when renaming or adding a program with a name already in use.

Choose **Browse X16 Files** to find a program or **Add a local program** to use one you have. If the selected program needs your first emulator, **Install emulator** and **Register existing** are available from Start.

Double-click an entry or select **Run** to launch it. The entry's saved configuration takes priority. **Start defaults** supplies an emulator and settings for entries without a configuration. The preview identifies the settings in use and **Copy PowerShell** copies the same command. The working directory is always the PRG's containing folder.

Use **Launch settings** to edit the selected PRG's name, path, and launch settings. **Duplicate entry** copies the entry so you can save another setup for the same PRG. **Remove from Start** in the program menu removes only the listing. **Delete files** removes the selected entries and deletes their PRG or BAS files after confirmation. Use Shift to select a range or Ctrl to select individual rows. Select every entry that shares a file before deleting it.

Selecting an emulator fills the form from that build's default profile or emulator defaults. Select a different profile to prefill its values. Save a copy of the resulting configuration or choose **Use Start defaults** to let the entry follow the Start list's defaults. An entry whose emulator was removed requires reassignment.

Use **Add a local program** or **Launch settings** to set the name, path, and launch settings in one dialog. Browse for a PRG, a plain-text BASIC program, or a ZIP. The launcher keeps ZIP contents together and asks which PRG or BAS to use when there are several. **Duplicate entry** copies those fields into a new entry. Companion files and folders stay on disk when you delete a program. A relative GIF output path is resolved against the PRG’s folder. On Start, **Save GIF** overrides the recording filename for launches while checked. Use **Browse** to choose the destination. The picker opens on the Desktop when the field is blank. Each Start entry remembers its own destination. Leaving the field blank uses a filename based on the entry ID in the program’s folder. The PRG’s saved settings stay intact.

## X16 Files

Expand a category to see its programs. Use **Downloadable only** to show entries with a ZIP or PRG link in the catalog. The filter works with the search box.

Browse the X16 Files catalog, open a project page, or choose **Install to Start** to download a ZIP or PRG from the catalog and register its PRG in Start. **Reinstall to Start** updates the existing entry while preserving its launch settings. The Start program menu offers **Open previous folder** and **Revert to previous install**, so you can recover saves from the previous folder. **Program information** shows available source and creator details. Use **Download and launch** to run a ZIP or PRG from the catalog immediately. The launcher keeps ZIP contents together and uses the catalog’s startup PRG when specified. With no startup hint, it uses the only PRG or asks you to choose among several. Catalog launches use Start defaults, or the default profile of the sole registered emulator, with **Run after loading** enabled.

## SD Card Kit

Browse the Commander X16 SD Card Starter Kit as a folder tree. Search by program or folder path and use **Refresh from SD Card Kit** to fetch the current listing. The tree shows PRGs, BAS files, ZIP packages, and the folders that contain them. Use **Install to Start**, **Reinstall to Start**, or **Download and launch**. Double-click a program to install or reinstall it. The first import downloads the kit for that revision so that programs can access companion files. Later imports reuse the local copy. ZIPs offer a startup picker when they contain multiple PRG or BAS files.

## Emulators and profiles

Use **Register existing** for an unpacked folder or executable, including renamed builds. **Install or update** installs official Windows releases side by side. **Edit emulator** updates the build name, executable path, and whether standard X16Emu options are supported. ROM and extra arguments are set in the build’s profile. For serial networking, select **R49 Win64** and check **Install Serial/Network overlay**. F.A.T. installs the verified overlay ZIP into the R49 folder and keeps the base ROM. **Enable serial card** starts checked for this build and can be turned off per profile or program. The control is disabled for stock emulators. The matching manually overlaid R49 installation can also be registered.

Each build starts with a **Default** profile and can have additional named profiles with **New profile**, **Edit**, **Duplicate**, **Delete**, and **Set as default**. Profiles prefill PRG settings and Start defaults. Saving either makes an independent copy, including the effective ROM and extra arguments. The standard controls show Commander X16 hardware choices. **CPU** offers 65C02 and 65C816. **Advanced hardware** contains custom clock and RAM values. Display scaling is available in the same editor.

**Open folder** opens the selected emulator’s directory in File Explorer. **Remove emulator** offers **Remove from list** to keep the folder on disk or **Delete emulator files** to permanently delete the folder after confirmation. PRGs referring to the build keep their options and ask for a new emulator. Profiles from removed builds remain available to copy.

## Linux and macOS ports

The port specification describes the launcher, X16 Files JSON feed, ZIP handling, and X16Emu command line. [SPECIFICATION.md](SPECIFICATION.md)

## Build on Windows

Use a Visual Studio Developer PowerShell with the .NET Framework 4.8 targeting pack and Inno Setup 6. Run .\Build.ps1 to compile the application and create dist\X16ForAllTime-Setup.exe. Use -ISCC if Inno Setup is installed elsewhere. Windows PowerShell 5.1 handles emulator release discovery and installation.

## Installing and removing emulators

The installer shows progress while checking releases, downloading, verifying, extracting and installing. **Overwrite existing installation** permanently deletes the previous emulator folder after the replacement is installed and registered. If replacement or registration fails, the launcher attempts to restore the original folder. Existing registrations keep their profiles and PRG assignments. PRG entries and their settings can be edited before an emulator is available.

## Contributing

AI agent-led contributions are preferred. Read the project guidance before starting work. [CONTRIBUTORS.md](CONTRIBUTORS.md)
