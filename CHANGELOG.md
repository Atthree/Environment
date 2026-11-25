# Changelog

## [0.1.0] - 2026-11-24
- Initial version: `EnvironmentPreset` assets with sun, sky (procedural / solid color / custom material),
  ambient, fog and URP post settings; five built-in presets (Day, Sunset, Night, Dungeon, Foggy).
- `EnvironmentController`: applies presets, smooth `TransitionTo`, `Apply` for raw settings, live editor
  preview when a preset is edited, sky reflections rendered into a cubemap (no baking), play-mode copies
  of the skybox material and Volume Profile.
- `TimeOfDay`: hour keys with blending across midnight, sun/moon path with horizon fade, running clock.
- `EnvironmentBuilder` + Environment window (Window > Atthree > Environment): build into the current or
  a new scene with Undo, per-scene assets, copy built-in presets to the project. Inspectors with
  preset buttons. Works without URP (post-processing skipped).
- Demo sample. 25 PlayMode tests (22 without URP).
