import re

with open('Views/ThemeSettingsWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

advanced_block = '''
                <!-- Advanced Colors -->
                <StackPanel Spacing="8">
                    <TextBlock Text="Расширенная настройка цветов" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                    <Border Background="{StaticResource PanelBrush}" BorderBrush="{StaticResource BorderEditorBrush}" BorderThickness="1" CornerRadius="8" Padding="12">
                        <StackPanel Spacing="12">
                            <TextBlock Text="Данный функционал находится в активной разработке. В будущих обновлениях здесь появится полноценная палитра для настройки каждого элемента (Background, Cards, Text, и т.д.). Пока что используйте выбор Акцентного цвета выше!" 
                                       Foreground="{StaticResource TextSecondaryBrush}" FontSize="12" TextWrapping="Wrap"/>
                            <Button Classes="primaryBtn" Content="Скоро..." IsEnabled="False" HorizontalAlignment="Left" Padding="16,8"/>
                        </StackPanel>
                    </Border>
                </StackPanel>
'''

# insert before Particles
text = text.replace('<!-- Particles -->', advanced_block + '\n                <!-- Particles -->')

with open('Views/ThemeSettingsWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
