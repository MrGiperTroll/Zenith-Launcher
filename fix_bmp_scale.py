import re

with open('Views/MainWindow.axaml.cs', 'r', encoding='utf-8') as f:
    text = f.read()

pattern = r'''var topLevel = Avalonia\.Controls\.TopLevel\.GetTopLevel\(this\);
                double scale = topLevel\?\.RenderScaling \?\? 1\.0;
                var bmp = new Avalonia\.Media\.Imaging\.RenderTargetBitmap\(new PixelSize\(\(int\)Math\.Ceiling\(_draggedCard\.Bounds\.Width \* scale\), \(int\)Math\.Ceiling\(_draggedCard\.Bounds\.Height \* scale\)\), new Vector\(96 \* scale, 96 \* scale\)\);'''

replacement = r'''var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
                double dpi = topLevel?.RenderScaling ?? 1.0;
                double lScale = (DataContext as ViewModels.MainWindowViewModel)?.LauncherScale ?? 1.0;
                double scale = dpi * lScale;
                var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Math.Ceiling(_draggedCard.Bounds.Width * scale), (int)Math.Ceiling(_draggedCard.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));'''

text = re.sub(pattern, replacement, text)

with open('Views/MainWindow.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(text)
