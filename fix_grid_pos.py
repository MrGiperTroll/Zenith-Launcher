import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('''        </Grid>
        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" Grid.ColumnSpan="2" ZIndex="9999"/>
    </Grid>
    </LayoutTransformControl>''', '''        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" Grid.ColumnSpan="2" ZIndex="9999"/>
        </Grid>
    </LayoutTransformControl>''')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
