with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Replace the specific block
start_marker = '<!-- Simple Theme Presets -->'
end_marker = '<!-- Discord RPC -->'

start_idx = text.find(start_marker)
end_idx = text.find(end_marker)

replacement = '''<!-- Edit Theme Card -->
                        <Border Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1" CornerRadius="10" Padding="12,10" Margin="0,0,0,0">
                            <StackPanel Spacing="6">
                                <TextBlock Text="Edit Theme" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                                <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
                                    <TextBlock Grid.Column="0" Text="Customize colors, animations, and the overall look and feel of Zenith." FontSize="12" Foreground="{StaticResource TextSecondaryBrush}" TextWrapping="Wrap" VerticalAlignment="Center"/>
                                    <Button Grid.Column="1" Classes="primaryBtn" Content="Customize" Command="{Binding OpenThemeEditorCommand}" Padding="16,8"/>
                                </Grid>
                            </StackPanel>
                        </Border>

                        '''

text = text[:start_idx] + replacement + text[end_idx:]

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
