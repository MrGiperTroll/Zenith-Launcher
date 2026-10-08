import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# I will replace:
#             <LayoutTransformControl.LayoutTransform>
#                 <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
#             </LayoutTransformControl.LayoutTransform>
#             <Grid ColumnDefinitions="Auto,*">
# with:
#             <LayoutTransformControl.LayoutTransform>
#                 <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
#             </LayoutTransformControl.LayoutTransform>
#             <Grid>
#             <Grid ColumnDefinitions="Auto,*">

text = text.replace('''            <LayoutTransformControl.LayoutTransform>
                <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
            </LayoutTransformControl.LayoutTransform>
            <Grid ColumnDefinitions="Auto,*">''', '''            <LayoutTransformControl.LayoutTransform>
                <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
            </LayoutTransformControl.LayoutTransform>
            <Grid>
            <Grid ColumnDefinitions="Auto,*">''')

# Now add </Grid> at the end
text = text.replace('''        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
    </LayoutTransformControl>''', '''        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
        </Grid>
    </LayoutTransformControl>''')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
