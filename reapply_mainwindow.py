import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Restore LayoutTransformControl and particle system
text = text.replace('''    <Panel Background="#080A0F">
        <Grid>
        <Grid ColumnDefinitions="Auto,*">''', '''    <Panel Background="{DynamicResource BgBrush}">
        <controls:ParticleSystemControl ParticleType="{Binding SelectedParticle}" IsHitTestVisible="False"/>
        <LayoutTransformControl>
            <LayoutTransformControl.LayoutTransform>
                <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
            </LayoutTransformControl.LayoutTransform>
            <Grid ColumnDefinitions="Auto,*">''')

# Close LayoutTransformControl and wrap DragOverlayPanel
text = text.replace('''    </Grid>
        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
    </Panel>
</Window>''', '''        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" Grid.ColumnSpan="2" ZIndex="9999"/>
    </Grid>
    </LayoutTransformControl>
    </Panel>
</Window>''')

# Re-apply ThemeSettingsView
text = text.replace('''<views:LauncherSettingsView IsVisible="{Binding IsSettingsTabAppearance}"/>''', '''<views:LauncherSettingsView IsVisible="{Binding IsSettingsTabAppearance}"/>
                <views:ThemeSettingsView IsVisible="{Binding IsSettingsTabTheme}"/>''')

# Update Top Header margins and separators if they were reverted
text = text.replace('<Grid Grid.Row="0" Margin="24,0,24,0" VerticalAlignment="Center" ColumnDefinitions="*,Auto">', '<Grid Grid.Row="0" Margin="8,0,24,0" VerticalAlignment="Center" ColumnDefinitions="*,Auto">')
text = text.replace('<Rectangle Width="1" Height="14" Fill="{StaticResource BorderDarkBrush}" Margin="2,0,2,0"/>', '')

# Remove extra         <Grid> which was before <Grid ColumnDefinitions="Auto,*">
text = text.replace('''    <Panel Background="#080A0F">
        <Grid ColumnDefinitions="Auto,*">''', '''    <Panel Background="{DynamicResource BgBrush}">
        <controls:ParticleSystemControl ParticleType="{Binding SelectedParticle}" IsHitTestVisible="False"/>
        <LayoutTransformControl>
            <LayoutTransformControl.LayoutTransform>
                <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
            </LayoutTransformControl.LayoutTransform>
            <Grid ColumnDefinitions="Auto,*">''')


with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
