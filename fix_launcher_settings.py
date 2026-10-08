import re

with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Replace the Simple theme card
pattern = r'<Border Background="\{StaticResource PanelBrushTransparent\}".*?<!-- Discord RPC -->'
replacement = r'''<Border Background="{StaticResource PanelBrushTransparent}" BorderBrush="{StaticResource BorderEditorBrush}" BorderThickness="1" CornerRadius="8" Padding="16" Margin="0,0,0,16">
                <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
                    <StackPanel Grid.Column="0" Spacing="4">
                        <TextBlock Text="Edit Theme" FontSize="14" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                        <TextBlock Text="Customize colors, animations, and the overall look and feel of Zenith." FontSize="12" Foreground="{StaticResource TextSecondaryBrush}" TextWrapping="Wrap"/>
                    </StackPanel>
                    <Button Grid.Column="1" Classes="primaryBtn" Content="Customize" Command="{Binding OpenThemeEditorCommand}" Padding="20,8"/>
                </Grid>
            </Border>

            <!-- Discord RPC -->'''

text = re.sub(pattern, replacement, text, flags=re.DOTALL)

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
