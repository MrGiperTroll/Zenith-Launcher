import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('''        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
    </Grid>
    </LayoutTransformControl>''', '''        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
    </Grid>
    </Grid>
    </LayoutTransformControl>''')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
