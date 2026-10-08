import re

with open('Views/MainWindow.axaml.cs', 'r', encoding='utf-8') as f:
    text = f.read()

pattern = r'''var topLevel = Avalonia\.Controls\.TopLevel\.GetTopLevel\(this\);
                double scale = topLevel\?\.RenderScaling \?\? 1\.0;
                var bmp = new Avalonia\.Media\.Imaging\.RenderTargetBitmap\(new PixelSize\(\(int\)Math\.Ceiling\(_draggedCard\.Bounds\.Width \* scale\), \(int\)Math\.Ceiling\(_draggedCard\.Bounds\.Height \* scale\)\), new Vector\(96 \* scale, 96 \* scale\)\);
                bmp\.Render\(_draggedCard\);
                
                _ghostRect = new Avalonia\.Controls\.Shapes\.Rectangle
                \{
                    Width = _draggedCard\.Bounds\.Width,
                    Height = _draggedCard\.Bounds\.Height,
                    Fill = new Avalonia\.Media\.ImageBrush\(bmp\) \{ Stretch = Avalonia\.Media\.Stretch\.Fill \},'''

replacement = r'''_ghostRect = new Avalonia.Controls.Shapes.Rectangle
                {
                    Width = _draggedCard.Bounds.Width,
                    Height = _draggedCard.Bounds.Height,
                    Fill = new Avalonia.Media.VisualBrush(_draggedCard) { Stretch = Avalonia.Media.Stretch.None, AlignmentX = Avalonia.Media.AlignmentX.Left, AlignmentY = Avalonia.Media.AlignmentY.Top },'''

text = re.sub(pattern, replacement, text)

# I also need to NOT hide the original _draggedCard because VisualBrush mirrors it!
# If I set Opacity=0.01, VisualBrush will be 0.01 opacity.
# So I'll set _draggedCard.Opacity = 0.0, but wait, VisualBrush will be invisible.
# I'll keep _draggedCard.Opacity = 1.0, but maybe I will set IsVisible = true but hide its contents?
# A better trick: Avalonia 11 VisualBrush CAN render invisible visuals if they are in the tree? No.
# I will just set Opacity = 0.01 on _draggedCard, but wait, then the VisualBrush will be transparent!
# Let's revert back to RenderTargetBitmap! RenderTargetBitmap renders it BEFORE we change Opacity to 0.01. So it captures it perfectly!
# Let's write the RenderTargetBitmap logic but cleaner.
