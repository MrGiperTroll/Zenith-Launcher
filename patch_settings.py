import re

with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Remove Discord Rich Presence from General Tab
discord_block = r'''                        <StackPanel Spacing="6" IsVisible="\{Binding IsSearchDiscordVisible\}">\s*<StackPanel Orientation="Horizontal" Spacing="8">\s*<CheckBox Content="\{l10n:Tr set_discord\}" IsChecked="\{Binding DiscordRpcEnabled\}" Foreground="\{StaticResource TextSecondaryBrush\}" FontSize="12" VerticalAlignment="Center"/>\s*</StackPanel>\s*<TextBlock Text="\{l10n:Tr set_discord_hint\}" FontSize="11" Foreground="\{StaticResource TextPlaceholderBrush\}"/>\s*</StackPanel>'''
content = re.sub(discord_block, '', content)

# 2. Replace Appearance tab contents with the new Launcher Settings layout
appearance_tab_start = r'                    <!-- Settings \(Appearance & Themes - Lunar Client Restrained Design\) -->\s*<StackPanel Spacing="16" IsVisible="\{Binding IsSettingsTabAppearance\}">\s*<TextBlock Text="\{l10n:Tr Key=nav_settings\}" FontSize="16" FontWeight="Bold" Foreground="\{StaticResource TextPrimaryBrush\}" Margin="0,0,0,4"/>'
appearance_tab_end = r'                    </StackPanel>\s*</StackPanel>\s*</ScrollViewer>'

pattern = appearance_tab_start + r'.*?' + appearance_tab_end

new_appearance = '''                    <!-- Launcher Settings -->
                    <StackPanel Spacing="16" IsVisible="{Binding IsSettingsTabAppearance}">
                        <TextBlock Text="{l10n:Tr Key=nav_settings}" FontSize="16" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}" Margin="0,0,0,4"/>

                        <Border Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1" CornerRadius="10" Padding="12,10">
                            <StackPanel Spacing="16">
                                <!-- Theme Customization -->
                                <StackPanel Spacing="6">
                                    <TextBlock Text="{l10n:Tr Key=theme_customize}" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                                    <Button Classes="primaryBtn" Command="{Binding OpenThemeSettingsCommand}" Content="{l10n:Tr Key=btn_customize}" HorizontalAlignment="Left" Padding="16,8"/>
                                </StackPanel>

                                <Rectangle Height="1" Fill="{StaticResource BorderStrongBrush}"/>

                                <!-- Discord RPC -->
                                <StackPanel Spacing="6" IsVisible="{Binding IsSearchDiscordVisible}">
                                    <CheckBox Content="{l10n:Tr set_discord}" IsChecked="{Binding DiscordRpcEnabled}" Foreground="{StaticResource TextSecondaryBrush}" FontSize="12" VerticalAlignment="Center"/>
                                    <TextBlock Text="{l10n:Tr set_discord_hint}" FontSize="11" Foreground="{StaticResource TextPlaceholderBrush}"/>
                                </StackPanel>
                                
                                <Rectangle Height="1" Fill="{StaticResource BorderStrongBrush}"/>

                                <!-- Launcher Scale -->
                                <StackPanel Spacing="6">
                                    <TextBlock Text="{l10n:Tr Key=settings_scale}" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                                    <Grid ColumnDefinitions="*,Auto">
                                        <Slider Minimum="0.5" Maximum="2.0" Value="{Binding LauncherScale}" TickFrequency="0.1" IsSnapToTickEnabled="True" VerticalAlignment="Center"/>
                                        <TextBlock Grid.Column="1" Text="{Binding LauncherScale, StringFormat='{}{0:F1}x'}" Margin="12,0,0,0" VerticalAlignment="Center" Foreground="{StaticResource TextPrimaryBrush}" FontWeight="Bold" Width="40"/>
                                    </Grid>
                                </StackPanel>
                            </StackPanel>
                        </Border>
                    </StackPanel>

                </StackPanel>
            </ScrollViewer>'''

content = re.sub(pattern, new_appearance, content, flags=re.DOTALL)

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(content)
