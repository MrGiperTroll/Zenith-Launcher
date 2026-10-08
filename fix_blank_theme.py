import re

with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<ScrollViewer x:Name="SettingsContentScroll" Grid.Column="1" Padding="24,20,28,20" VerticalScrollBarVisibility="Auto">',
                    '<ScrollViewer x:Name="SettingsContentScroll" Grid.Column="1" Padding="24,20,28,20" VerticalScrollBarVisibility="Auto" IsVisible="{Binding !IsSettingsTabTheme}">')

text = text.replace('              </ScrollViewer>\n        </Grid>',
                    '              </ScrollViewer>\n              <views:ThemeSettingsView Grid.Column="1" IsVisible="{Binding IsSettingsTabTheme}" Padding="24,20,28,20"/>\n        </Grid>')

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
