# Project Rules: Anti-AI-Slop & Design Guidelines

Strict rules for all frontend and UI modifications in ZenithMGT / CustomMcLauncher:

1. **Never Change The Launcher Color Palette**:
   - Strictly preserve the original color palette, brushes (`ThemeBrushes`, background `#121214`, card `#1E1E22`, accent colors).
   - Never inject alien gradient colors, purple/cyan glows, or flashy themes.
2. **No AI Slop & Button Hover Integrity**:
   - Ban gratuitous backdrop-blur, heavy drop shadows, and oversized bubble border radii.
   - Design must remain utilitarian, clean, crisp, and high-performance.
   - **CRITICAL - Button Hover State**: Primary/accent buttons (`primaryBtn`) must NEVER turn flat dark gray, washed-out outline, or transparent on hover. Hover must always illuminate or maintain the accent color (`AccentBrushHover`).
   - **No Nested Buttons**: NEVER nest a `<Button>` inside another `<Button>`. Local `<Button.Styles>` in outer buttons leak to child buttons and break their hover states. Use `Border` with `Tapped` or clean MVVM commands instead.
3. **Smooth Performance & Zero Layout Jitter**:
   - Use UI virtualization for large lists and async thumbnail loading/caching.
   - Smooth 60 FPS scrolling and responsive controls.
   - **Zero 1px Jitter**:
     - Always enforce `UseLayoutRounding="True"` on `<Window>`, root containers, `LayoutTransformControl`, and view panes.
     - Never apply `RenderTransform="scale(...)"` to tabs, sidebar items, or navigation buttons (`sideTab`, `catalogTabBtn`, `navBtn`, etc.) on `:pressed`.
     - Never use `ObjectConverters.IsNotNull` on status text in `Auto` rows with margins (use `IsNotEmpty` to avoid empty strings reserving layout margins).
     - Keep `ScrollViewer.VerticalScrollBarVisibility="Visible"` on dynamic item lists to avoid scrollbar gutter pop-in/pop-out.
     - Reset scroll offsets synchronously (`Offset = Vector.Zero`) on tab switches rather than deferred `Dispatcher.UIThread.Post(..., Loaded)`.

