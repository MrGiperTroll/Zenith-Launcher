import re

with open('Views/ThemeSettingsView.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('Command="{Binding OpenSettingsDialogCommand}"', 'Command="{Binding CloseThemeEditorCommand}"')

old_btn = '''<Button x:CompileBindings="False"
                                                Command="{Binding [ItemsControl].DataContext.RequestApplyThemeCommand}"
                                                CommandParameter="{Binding HexColor}"
                                                Background="{StaticResource PanelBrushTransparent}" 
                                                BorderBrush="{StaticResource BorderEditorBrush}" BorderThickness="1" 
                                                CornerRadius="8" Padding="12,8" Margin="0,0,8,8" Cursor="Hand">
                                            <StackPanel Orientation="Horizontal" Spacing="8">
                                                <Ellipse Width="16" Height="16" Fill="{Binding ColorBrush}"/>
                                                <TextBlock Text="{Binding DisplayName}" FontSize="13" FontWeight="SemiBold" Foreground="{StaticResource TextSecondaryBrush}" VerticalAlignment="Center"/>
                                            </StackPanel>
                                        </Button>'''
new_btn = '''<Button x:CompileBindings="False"
                                                Command="{Binding [ItemsControl].((vm:MainWindowViewModel)DataContext).ChooseThemeCommand}"
                                                CommandParameter="{Binding HexColor}"
                                                Classes="subBtn"
                                                CornerRadius="8" Padding="12,8" Margin="0,0,8,8" Cursor="Hand">
                                            <StackPanel Orientation="Horizontal" Spacing="8">
                                                <Ellipse Width="16" Height="16" Fill="{Binding ColorBrush}" Stroke="{StaticResource BorderEditorBrush}" StrokeThickness="1"/>
                                                <TextBlock Text="{Binding DisplayName}" FontSize="13" FontWeight="SemiBold" Foreground="{StaticResource TextPrimaryBrush}" VerticalAlignment="Center"/>
                                            </StackPanel>
                                        </Button>'''
text = text.replace(old_btn, new_btn)

with open('Views/ThemeSettingsView.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
