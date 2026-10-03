---
name: anti-ai-slop-ui
description: "Anti-AI-Slop frontend and UI design standards. Enforces clean, restrained, utilitarian desktop UI design. Bans tacky gradients, pseudo-glassmorphism, neon glows, oversized corner radii, and bloated shadow stacks. Preserves existing design systems and palettes."
---

# Anti-AI-Slop Frontend & UI Guidelines

Derived from proven developer standards (LeoStehlik/no-slop-ui, Nutlope/hallmark, and industry desktop design standards).

## Core Philosophy
- **Utilitarian, Honest, Restrained**: The goal of the UI is to be a fast, clear, functional work surface. Not flashy. Not dramatic.
- **Strict Respect for Existing Design System & Palette**: Never change the existing project color palette, theme brushes, or style tokens. Do not introduce foreign aesthetic fads.
- **Performance First**: Smooth native rendering (60+ FPS), zero pointless GPU strain, zero unnecessary visual compositing layers.

## Strictly Banned UI Anti-Patterns ("AI Slop")
1. **No Gratuitous Gradients**:
   - Banned: Multi-color diagonal gradient backgrounds, gradient text, radial glowing spots, rainbow or purple/cyan borders.
   - Allowed: Clean solid fills or very subtle, barely perceptible single-hue tonal shifts strictly defined by existing theme brushes.
2. **No Pseudo-Glassmorphism & Gratuitous Blur**:
   - Banned: Excessive `backdrop-filter`, `BlurRadius`, semi-transparent frosted cards stacked everywhere that destroy text contrast and kill rendering performance.
   - Allowed: Solid backgrounds or opaque theme-backed panels with crisp 1px borders.
3. **No Neon / Cyberpunk / Purple-Blue Glows**:
   - Banned: Neon purple/cyan edge glows, drop-shadows with saturated colors, artificial illumination around cards.
   - Allowed: High-contrast, legible neutral borders (`#2A2D32`, `#3E4451`, etc.) matching the launcher's dark theme.
4. **No Oversized Corner Radii (Pill-ification)**:
   - Banned: 24px-36px bubble corners on square cards, pill buttons everywhere, balloon-like containers.
   - Allowed: Restrained, crisp radii: 4px to 8px for cards and controls, matching existing XAML styles.
5. **No Over-Engineered Shadow Stacks**:
   - Banned: Deep 3-layer fuzzy drop shadows that blur into the background and drop contrast.
   - Allowed: Subtle, tight shadows (e.g. `0 2px 6px #10000000` or 1px border outlines) or flat elevation.
6. **No Contrast Degradation**:
   - Banned: Low-contrast dark gray text on slightly darker gray backgrounds, dimmed badges that are illegible.
   - Allowed: WCAG AA+ readable foreground colors (`#E0E0E0`, `#FFFFFF`, muted `#A0A0A0`).
7. **No AI Animation Slop**:
   - Banned: Bouncy springs, slow 800ms morphs, spinning icons on simple interactions, disruptive layout reflows on hover.
   - Allowed: Snappy, purposeful micro-transitions (100–150ms ease-out) for pointer press/hover feedback.

## Application to Avalonia UI / XAML in this Project
- Use existing ThemeBrushes and StaticResources from `App.axaml`.
- Maintain clean layout containers: avoid deep nesting, enable control virtualization.
- Keep controls responsive and keyboard/mouse accessible.
