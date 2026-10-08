import re

with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

pattern = r'<!-- Theme Presets Card -->.*?</Border>\s*</StackPanel>\s*</StackPanel>\s*</ScrollViewer>'

replacement = r'''<!-- Edit Theme Card -->
                        <Border Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1" CornerRadius="10" Padding="12,10" IsVisible="{Binding IsSearchAppearanceVisible}">
                            <StackPanel Spacing="6">
                                <TextBlock Text="Edit Theme" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                                <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
                                    <TextBlock Grid.Column="0" Text="Customize colors, animations, and the overall look and feel of Zenith." FontSize="12" Foreground="{StaticResource TextSecondaryBrush}" TextWrapping="Wrap" VerticalAlignment="Center"/>
                                    <Button Grid.Column="1" Classes="primaryBtn" Content="Customize" Command="{Binding OpenThemeEditorCommand}" Padding="16,8"/>
                                </Grid>
                            </StackPanel>
                        </Border>

                        <!-- Launcher Scale -->
                        <Border Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1" CornerRadius="10" Padding="12,10">
                            <StackPanel Spacing="6">
                                <TextBlock Text="{l10n:Tr Key=settings_scale}" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                                <Grid ColumnDefinitions="*,Auto">
                                    <Slider Minimum="0.5" Maximum="2.0" Value="{Binding LauncherScale}" TickFrequency="0.05" IsSnapToTickEnabled="True" VerticalAlignment="Center"/>
                                    <TextBlock Grid.Column="1" Text="{Binding LauncherScale, StringFormat='{}{0:P0}'}" Margin="12,0,0,0" VerticalAlignment="Center" Foreground="{StaticResource TextPrimaryBrush}" FontWeight="Bold" Width="40" TextAlignment="Right"/>
                                </Grid>
                            </StackPanel>
                        </Border>
                      </StackPanel>
                  </StackPanel>
              </ScrollViewer>'''

text = re.sub(pattern, replacement, text, flags=re.DOTALL)

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
