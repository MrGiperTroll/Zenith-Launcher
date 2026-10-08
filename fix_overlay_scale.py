import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('''    </LayoutTransformControl>
        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
    </Panel>''', '''        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
        </Grid>
    </LayoutTransformControl>
    </Panel>''')

# We need to make sure we remove the existing </Grid> before LayoutTransformControl that we swapped earlier.
text = text.replace('''    </Grid>
    </LayoutTransformControl>
        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>''', '''        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
    </Grid>
    </LayoutTransformControl>''')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
