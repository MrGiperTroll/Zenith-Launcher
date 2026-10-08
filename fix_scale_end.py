import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

pattern = r'(</Grid>\s*<!-- DragOverlayPanel: topmost layer for drag ghost rendering -->\s*<Canvas x:Name="DragOverlayPanel".*?>)'
repl = r'</LayoutTransformControl>\n        \1'
text = re.sub(pattern, repl, text, count=1)

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
