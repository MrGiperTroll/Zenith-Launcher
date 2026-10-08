import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# I will wrap the Grid inside LayoutTransformControl
text = text.replace('''    <Panel Background="#080A0F">
        <Grid>
        <Grid ColumnDefinitions="Auto,*">''', '''    <Panel Background="{StaticResource BgBrush}">
        <controls:ParticleSystemControl ParticleType="{Binding SelectedParticle}" IsHitTestVisible="False"/>
        <LayoutTransformControl>
            <LayoutTransformControl.LayoutTransform>
                <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
            </LayoutTransformControl.LayoutTransform>
            <Grid>
            <Grid ColumnDefinitions="Auto,*">''')

text = text.replace('''        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" Grid.ColumnSpan="2" ZIndex="9999"/>
    </Grid>
    </LayoutTransformControl>''', '''        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
        </Grid>
    </LayoutTransformControl>''')

# Since I git checked out, the end of the file is probably different.
# Let's verify what the end of the file currently is.
