# Tool plugins

Each tooling type is a separate assembly (`UrHmi.Tools.<Name>`) loaded at runtime from the app `plugins/` output folder.

The core solution must not add a `ProjectReference` to a concrete tool project. `UrHmi.App` copies nothing by name; each plugin copies its own DLL into the app output after build.

This cell ships `UrHmi.Tools.Screwdriver` (atornillado logic and ViewModel) plus `UrHmi.Tools.Screwdriver.Ui` (WPF view factory). Both load in one plugin context so the DataTemplate key matches the ViewModel type. Tool Management hosts the view from `IToolViewFactory.CreateView`. Enable it with `tools.enabled` / `tools.selectedId` in `hmi.json`. A plugin that fails to load raises an application alarm and does not take down the HMI.

SpinBridge config lives in the plugin’s `screwdriver.json` (IP `10.42.0.1`, OPC UA `opc.tcp://10.42.0.1:4840`). That address is not a secret and must not go in `hmi.json` or `ISecretStore`. The plugin opens that OPC UA session itself and maps LatestResult final torque (Nm) and final angle (°) onto the screwdriver UI with Live / Stale / Unavailable. Those values are not UR or PLC tags.
