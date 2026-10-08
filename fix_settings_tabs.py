import re

with open('Views/LauncherSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

pattern = r'(<StackPanel Spacing="16" IsVisible="\{Binding IsSettingsTabAppearance\}">.*?)(?:</StackPanel>\s*</Border>\s*</StackPanel>)'

new_block = '''<StackPanel Spacing="16" IsVisible="{Binding IsSettingsTabAppearance}">
                        <TextBlock Text="{l10n:Tr Key=nav_settings}" FontSize="16" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}" Margin="0,0,0,4"/>

                        <!-- Simple Theme Presets -->
                        <Border Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1" CornerRadius="10" Padding="12,10">
                            <StackPanel Spacing="12">
                                <TextBlock Text="{l10n:Tr Key=theme_simple}" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                                
                                <ItemsControl ItemsSource="{Binding AvailableThemes}">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <WrapPanel Orientation="Horizontal"/>
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate x:CompileBindings="False">
                                            <Button Command="{Binding [ItemsControl].DataContext.ChooseThemeCommand}"
                                                    CommandParameter="{Binding HexColor}"
                                                    Classes="subBtn"
                                                    Margin="0,0,8,8"
                                                    Padding="12,8"
                                                    CornerRadius="8"
                                                    VerticalContentAlignment="Center"
                                                    HorizontalContentAlignment="Center">
                                                <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                                                    <Border Width="16" Height="16" CornerRadius="8" Background="{Binding ColorBrush}" BorderBrush="{StaticResource BorderEditorBrush}" BorderThickness="1" VerticalAlignment="Center"/>
                                                    <TextBlock Text="{Binding DisplayName}" FontSize="12" FontWeight="SemiBold" VerticalAlignment="Center"/>
                                                </StackPanel>
                                            </Button>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>

                                <StackPanel Orientation="Horizontal" Spacing="12">
                                    <Button Classes="primaryBtn" Command="{Binding OpenThemeSettingsCommand}" Content="{l10n:Tr Key=theme_advanced}" Padding="16,8"/>
                                </StackPanel>
                            </StackPanel>
                        </Border>

                        <!-- Discord RPC -->
                        <Border Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1" CornerRadius="10" Padding="12,10" IsVisible="{Binding IsSearchDiscordVisible}">
                            <StackPanel Spacing="6">
                                <CheckBox Content="{l10n:Tr set_discord}" IsChecked="{Binding DiscordRpcEnabled}" Foreground="{StaticResource TextSecondaryBrush}" FontSize="12" VerticalAlignment="Center"/>
                                <TextBlock Text="{l10n:Tr set_discord_hint}" FontSize="11" Foreground="{StaticResource TextPlaceholderBrush}"/>
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
                    </StackPanel>'''

# Because dotall regex might match too much, I will use a more precise replacement.
# I'll just find the exact block and replace it.
text_to_replace = re.search(r'<StackPanel Spacing="16" IsVisible="\{Binding IsSettingsTabAppearance\}">.*?</StackPanel>\s*</Border>\s*</StackPanel>', text, flags=re.DOTALL)
if text_to_replace:
    text = text.replace(text_to_replace.group(0), new_block)
else:
    print("Could not find block!")

with open('Views/LauncherSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
