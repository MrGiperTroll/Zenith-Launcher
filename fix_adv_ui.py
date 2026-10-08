import re

with open('Views/ThemeSettingsWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

advanced_block = '''
                <!-- Advanced Colors -->
                <StackPanel Spacing="8">
                    <TextBlock Text="Расширенная настройка цветов" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                    <Border Background="{StaticResource PanelBrush}" BorderBrush="{StaticResource BorderEditorBrush}" BorderThickness="1" CornerRadius="8" Padding="12">
                        <StackPanel Spacing="12">
                            <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
                                <TextBlock Text="Фон (Background)" FontSize="12" Foreground="{StaticResource TextSecondaryBrush}" VerticalAlignment="Center"/>
                                <TextBox Grid.Column="1" Classes="darkInput" Text="{Binding BgHex, Mode=TwoWay}" PlaceholderText="#080A0F" Width="100"/>
                            </Grid>
                            <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
                                <TextBlock Text="Карточки (Surface)" FontSize="12" Foreground="{StaticResource TextSecondaryBrush}" VerticalAlignment="Center"/>
                                <TextBox Grid.Column="1" Classes="darkInput" Text="{Binding SurfaceHex, Mode=TwoWay}" PlaceholderText="#0D111A" Width="100"/>
                            </Grid>
                            <Grid ColumnDefinitions="*,Auto" VerticalAlignment="Center">
                                <TextBlock Text="Панели (Panel)" FontSize="12" Foreground="{StaticResource TextSecondaryBrush}" VerticalAlignment="Center"/>
                                <TextBox Grid.Column="1" Classes="darkInput" Text="{Binding PanelHex, Mode=TwoWay}" PlaceholderText="#121722" Width="100"/>
                            </Grid>
                            
                            <Button Classes="primaryBtn" Command="{Binding RequestApplyThemeCommand}" Content="Применить цвета" HorizontalAlignment="Right" Padding="16,8" Margin="0,4,0,0"/>
                        </StackPanel>
                    </Border>
                </StackPanel>
'''

# Find the placeholder and replace it
text = re.sub(r'<!-- Advanced Colors -->.*?</StackPanel>\s*</Border>\s*</StackPanel>', advanced_block, text, flags=re.DOTALL)

with open('Views/ThemeSettingsWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
