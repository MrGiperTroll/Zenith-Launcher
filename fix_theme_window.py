import re

with open('Views/ThemeSettingsWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

new_content = '''<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:CustomMcLauncher.ViewModels"
        xmlns:l10n="using:CustomMcLauncher.Extensions"
        xmlns:services="using:CustomMcLauncher.Services"
        x:Class="CustomMcLauncher.Views.ThemeSettingsWindow"
        x:DataType="vm:MainWindowViewModel"
        Title="{l10n:Tr Key=theme_advanced}"
        Width="640" Height="480"
        WindowStartupLocation="CenterOwner"
        Background="{StaticResource BgBrush}"
        TransparencyLevelHint="None">
        
    <Window.Styles>
        <Style Selector="TextBox.darkInput">
            <Setter Property="Background" Value="{StaticResource SurfaceBrush}"/>
            <Setter Property="Foreground" Value="{StaticResource TextPrimaryBrush}"/>
            <Setter Property="BorderBrush" Value="{StaticResource BorderStrongBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="CornerRadius" Value="8"/>
            <Setter Property="Padding" Value="10,8"/>
            <Setter Property="SelectionBrush" Value="{DynamicResource AccentBrush}"/>
            <Setter Property="SelectionForegroundBrush" Value="{DynamicResource OnAccentBrush}"/>
        </Style>
        <Style Selector="Button.primaryBtn">
            <Setter Property="Background" Value="{DynamicResource AccentBrush}"/>
            <Setter Property="Foreground" Value="{DynamicResource OnAccentBrush}"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="CornerRadius" Value="8"/>
            <Setter Property="Padding" Value="14,8"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
        </Style>
        <Style Selector="Button.primaryBtn:pointerover /template/ ContentPresenter#PART_ContentPresenter">
            <Setter Property="Background" Value="{DynamicResource AccentBrushHover}"/>
        </Style>
    </Window.Styles>

    <Grid RowDefinitions="Auto,*">
        <!-- Header -->
        <Border Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="0,0,0,1" Padding="20,16">
            <TextBlock Text="{l10n:Tr Key=theme_advanced}" FontSize="18" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
        </Border>

        <ScrollViewer Grid.Row="1" Margin="20" Padding="0,0,16,0">
            <StackPanel Spacing="24">
                
                <!-- Custom Hex Color -->
                <StackPanel Spacing="8">
                    <TextBlock Text="Custom HEX Color" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                    <Border Background="{StaticResource PanelBrush}" BorderBrush="{StaticResource BorderEditorBrush}" BorderThickness="1" CornerRadius="8" Padding="12">
                        <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="12" VerticalAlignment="Center">
                            <Border Grid.Column="0" Background="{Binding PreviewAccentBrush}" CornerRadius="6" Padding="12,6" VerticalAlignment="Center">
                                <TextBlock Text="Preview" FontSize="11" FontWeight="Bold" Foreground="{DynamicResource OnAccentBrush}"/>
                            </Border>
                            
                            <TextBox Grid.Column="1" Classes="darkInput" Text="{Binding CustomThemeHex, Mode=TwoWay}" PlaceholderText="#3B82F6" Height="30" MinHeight="30" Padding="8,0" VerticalContentAlignment="Center" FontSize="12" FontWeight="SemiBold"/>
                            
                            <Button Grid.Column="2" Classes="primaryBtn" Command="{Binding RequestApplyThemeCommand}" Height="30" Padding="16,0" VerticalAlignment="Center" Content="{l10n:Tr Key=set_apply}"/>
                        </Grid>
                    </Border>
                </StackPanel>

                <!-- Particles -->
                <StackPanel Spacing="8">
                    <TextBlock Text="{l10n:Tr Key=theme_particles}" FontSize="13" FontWeight="Bold" Foreground="{StaticResource TextPrimaryBrush}"/>
                    <Border Background="{StaticResource PanelBrush}" BorderBrush="{StaticResource BorderEditorBrush}" BorderThickness="1" CornerRadius="8" Padding="12">
                        <StackPanel Spacing="16">
                            <TextBlock Text="Выберите эффект для фона:" FontSize="13" Foreground="{StaticResource TextSecondaryBrush}"/>
                            <ComboBox SelectedItem="{Binding SelectedParticle}" HorizontalAlignment="Stretch">
                                <ComboBoxItem Content="None"/>
                                <ComboBoxItem Content="Snowflakes"/>
                                <ComboBoxItem Content="Leaves"/>
                            </ComboBox>
                        </StackPanel>
                    </Border>
                </StackPanel>

            </StackPanel>
        </ScrollViewer>
    </Grid>
</Window>
'''

with open('Views/ThemeSettingsWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(new_content)
