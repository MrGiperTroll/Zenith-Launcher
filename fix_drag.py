import re

with open('Views/MainWindow.axaml.cs', 'r', encoding='utf-8') as f:
    text = f.read()

pattern = r'var bmp = new Avalonia\.Media\.Imaging\.RenderTargetBitmap\(new PixelSize\(\(int\)_draggedCard\.Bounds\.Width, \(int\)_draggedCard\.Bounds\.Height\), new Vector\(96, 96\)\);'
replacement = r'''var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
                double scale = topLevel?.RenderScaling ?? 1.0;
                var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Math.Ceiling(_draggedCard.Bounds.Width * scale), (int)Math.Ceiling(_draggedCard.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));'''
text = re.sub(pattern, replacement, text)

# Update Stretch mode
text = text.replace('Stretch = Avalonia.Media.Stretch.None', 'Stretch = Avalonia.Media.Stretch.Fill')

# Change delay 400 -> 150
text = text.replace('TimeSpan.FromMilliseconds(400)', 'TimeSpan.FromMilliseconds(150)')

with open('Views/MainWindow.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(text)
