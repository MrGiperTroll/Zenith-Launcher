import re

with open('Views/ThemeSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Fix Back button
text = text.replace('Command="{Binding OpenSettingsDialogCommand}"', 'Command="{Binding CloseThemeEditorCommand}"')

# Fix Presets Command and Styling
text = text.replace('Command="{Binding [ItemsControl].DataContext.RequestApplyThemeCommand}"', 'Command="{Binding [ItemsControl].((vm:MainWindowViewModel)DataContext).ChooseThemeCommand}"')
text = text.replace('Background="{StaticResource PanelBrushTransparent}"', 'Classes="subBtn"')
text = text.replace('BorderBrush="{StaticResource BorderEditorBrush}" BorderThickness="1"', '')
text = text.replace('Foreground="{StaticResource TextSecondaryBrush}"', 'Foreground="{StaticResource TextPrimaryBrush}"')

# Also, wait! If I replace Foreground="{StaticResource TextSecondaryBrush}" globally it might mess up other things!
