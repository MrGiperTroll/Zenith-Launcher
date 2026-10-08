import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<Rectangle Width="1" Height="14" Fill="{StaticResource BorderDarkBrush}" Margin="4,0,6,0"/>', '')
text = text.replace('Margin="8,0,24,0" VerticalAlignment="Center" ColumnDefinitions="*,Auto"', 'Margin="0,0,24,0" VerticalAlignment="Center" ColumnDefinitions="*,Auto"')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
