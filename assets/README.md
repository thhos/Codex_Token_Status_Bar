# Launcher icon

The independent app icon combines a terminal symbol with a segmented credit meter. It is not an official OpenAI logo.

- `app-icon.png`: generated master image, RGBA with transparent corners.
- `app-icon.ico`: Windows icon container, 16/24/32/48/64/128/256 px, converted from the master without changing the design.
- Used by the executable resource and the generated launcher shortcut.

Created with the built-in image generation tool on 2026-09-17. Generation prompt:

> Create a single polished Windows desktop launcher icon for an independent application named Codex Token Status Bar. Square 1024x1024 PNG with truly transparent outside background. A large dark graphite rounded squircle, occupying about 90% of canvas. Inside, a bold friendly terminal symbol >_ in soft icy blue, and below it one thick horizontal usage meter with three mint-green rounded segments and a subtle dark unfilled end. Minimal geometric graphic, crisp strong silhouette, generous spacing, flat colors with very restrained soft highlight. Readable at 32px. Centered front-facing, no perspective, no decorative sparkles, no text or numbers, no OpenAI logo, no surrounding scene. The symbol and credit meter should feel like one coherent app icon. Keep all details chunky and use at most graphite, icy blue, mint, and a tiny muted gray.

Validation: all seven ICO sizes decode successfully; both the executable build and launcher use the icon. No image tooling is required to build or run the project.
