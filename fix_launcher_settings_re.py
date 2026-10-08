import re

with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Replace the Simple theme card (which has Background="{StaticResource SurfaceBrush}")
pattern = r'<Border Background="\{StaticResource SurfaceBrush\}".*?<!-- Discord RPC -->'
replacement = r'''<Border Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1" CornerRadius="10" Padding="12,10" Margin="0,0,0,0">
                            <StackPanel Spacing="6">
                                <TextBlock Text="Edit Theme" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                                <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
                                    <TextBlock Grid.Column="0" Text="Customize colors, animations, and the overall look and feel of Zenith." FontSize="12" Foreground="{StaticResource TextSecondaryBrush}" TextWrapping="Wrap" VerticalAlignment="Center"/>
                                    <Button Grid.Column="1" Classes="primaryBtn" Content="Customize" Command="{Binding OpenThemeEditorCommand}" Padding="16,8"/>
                                </Grid>
                            </StackPanel>
                        </Border>

                        <!-- Discord RPC -->'''

# Only replace the FIRST match which is the Theme card
text = re.sub(pattern, replacement, text, count=1, flags=re.DOTALL)

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
