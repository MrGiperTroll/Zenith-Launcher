import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Currently it is:
#         <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
#         <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
#     </Grid>
#     </LayoutTransformControl>
#     </Panel>
# Which seems correct if LayoutTransformControl started and then Grid started.
# Let's check what started on line 364.
