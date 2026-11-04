# Application mark

`AppMark.png` is the production 256px PNG frame of the existing multi-resolution VP dialogue-panel icon, originally generated with the built-in image tool on 18 October 2026. The oversized 1254px original remains in Git history; it is not needed at runtime. The owner requested white, minimal, flat artwork with a dialogue/lettermark identity; no avatar character or VR headset. VP refers to VRChat Projects. It is not an official VRChat, Unity or Creator Companion logo.

Final prompt: “One bold minimal white rectangular speech panel/frame with the exact letters VP inside in thick clean geometric sans-serif lettering; small offset lower-right tail, original proportions and softly squared corners, dark #202326 background, crisp flat 2D, no texture, gradient, shadows, person, headset, cube or arrows; centered with generous margin, legible at 24 pixels.”

`scripts/Build-AppIcon.ps1` converts this source into the multi-resolution Windows ICO (16–256 pixels). The GUI executable, title bar, taskbar/shortcuts and installer share the mark. The source is bundled locally; no generated-image external path is required at runtime. Other template package tiles are unused by the unpackaged desktop build.
