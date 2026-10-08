import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Remove the wrapper Grid start
text = text.replace('''            <LayoutTransformControl.LayoutTransform>
                <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
            </LayoutTransformControl.LayoutTransform>
            <Grid>
            <Grid ColumnDefinitions="Auto,*">''', '''            <LayoutTransformControl.LayoutTransform>
                <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
            </LayoutTransformControl.LayoutTransform>
            <Grid ColumnDefinitions="Auto,*">''')

# Remove the wrapper Grid end
text = text.replace('''        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
        </Grid>
    </LayoutTransformControl>''', '''        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" Grid.ColumnSpan="2" ZIndex="9999"/>
    </Grid>
    </LayoutTransformControl>''')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
