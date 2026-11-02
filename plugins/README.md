# Tool plugins

Each tooling type is a separate assembly (`UrHmi.Tools.<Name>`) loaded at runtime from the app `plugins/` output folder.

The core solution must not add a `ProjectReference` to a concrete tool project. `UrHmi.App` copies nothing by name; each plugin copies its own DLL into the app output after build.

This cell ships `UrHmi.Tools.Screwdriver` (atornillado logic and ViewModel) plus `UrHmi.Tools.Screwdriver.Ui` (WPF view factory). Enable it with `tools.enabled` / `tools.selectedId` in `hmi.json`. A plugin that fails to load raises an application alarm and does not take down the HMI.
