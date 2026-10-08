import re

with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Change the sidebar text for Appearance tab from nav_settings to "Launcher"
text = text.replace('Text="{l10n:Tr Key=nav_settings}" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center" Margin="10,0,0,0" Foreground="{Binding IsSettingsTabAppearance', 'Text="Launcher" FontSize="13" FontWeight="SemiBold" VerticalAlignment="Center" Margin="10,0,0,0" Foreground="{Binding IsSettingsTabAppearance')

# Change the header text of the Appearance section from nav_settings to "Launcher"
text = text.replace('Text="{l10n:Tr Key=nav_settings}" FontSize="16" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}" Margin="0,0,0,4"', 'Text="Launcher Settings" FontSize="16" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}" Margin="0,0,0,4"')

# We need to move the Discord RPC section.
discord_block = '''                        <StackPanel Spacing="6" IsVisible="{Binding IsSearchDiscordVisible}">
                            <StackPanel Orientation="Horizontal" Spacing="8">
                                <CheckBox Content="{l10n:Tr set_discord}" IsChecked="{Binding DiscordRpcEnabled}" Foreground="{StaticResource TextSecondaryBrush}" FontSize="12" VerticalAlignment="Center"/>
                            </StackPanel>
                            <TextBlock Text="{l10n:Tr set_discord_hint}" FontSize="11" Foreground="{StaticResource TextPlaceholderBrush}"/>
                        </StackPanel>
'''

if discord_block in text:
    text = text.replace(discord_block, '')
    
    # insert discord block at the beginning of the Appearance tab content (under the Launcher Settings header)
    appearance_header = '<TextBlock Text="Launcher Settings" FontSize="16" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}" Margin="0,0,0,4"/>\n'
    text = text.replace(appearance_header, appearance_header + '\n' + discord_block)
else:
    print("Could not find exact discord block to move.")

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)

print("Modified LauncherSettingsView.axaml")
