import re

with open('Views/ThemeSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('xmlns:services="using:CustomMcLauncher.Services"', 'xmlns:services="using:CustomMcLauncher.Services"\n             xmlns:cp="using:Avalonia.Controls.ColorPicker"')

# We will replace the TextBox and Border for each color with a ColorPicker!
# Wait, let's keep the TextBox for hex input, and replace the Border with a ColorPicker.
# For BgHex:
text = text.replace('<Border Grid.Column="2" Width="24" Height="24" CornerRadius="4" Background="{Binding BgHex}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1"/>',
                    '<ColorPicker Grid.Column="2" Color="{Binding BgHex, Mode=TwoWay}" Width="32" Height="32"/>')

text = text.replace('<Border Grid.Column="2" Width="24" Height="24" CornerRadius="4" Background="{Binding SurfaceHex}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1"/>',
                    '<ColorPicker Grid.Column="2" Color="{Binding SurfaceHex, Mode=TwoWay}" Width="32" Height="32"/>')

text = text.replace('<Border Grid.Column="2" Width="24" Height="24" CornerRadius="4" Background="{Binding PanelHex}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1"/>',
                    '<ColorPicker Grid.Column="2" Color="{Binding PanelHex, Mode=TwoWay}" Width="32" Height="32"/>')

text = text.replace('<Border Grid.Column="2" Width="24" Height="24" CornerRadius="4" Background="{Binding CustomThemeHex}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1"/>',
                    '<ColorPicker Grid.Column="2" Color="{Binding CustomThemeHex, Mode=TwoWay}" Width="32" Height="32"/>')


with open('Views/ThemeSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
