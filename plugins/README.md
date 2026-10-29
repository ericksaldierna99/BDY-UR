# Tool plugins

Each tooling type is a separate assembly (`UrHmi.Tools.<Name>`) loaded from this directory or registered in the composition root.

The core solution must not add a `ProjectReference` to a concrete tool project. Phase 0 ships no production tool plugin.
