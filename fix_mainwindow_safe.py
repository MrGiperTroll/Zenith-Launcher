import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# Change left margin for Top Header
text = text.replace('<Grid Grid.Row="0" Margin="24,0,24,0" VerticalAlignment="Center" ColumnDefinitions="*,Auto">', '<Grid Grid.Row="0" Margin="8,0,24,0" VerticalAlignment="Center" ColumnDefinitions="*,Auto">')

# Remove separator
text = text.replace('<Rectangle Width="1" Height="14" Fill="{StaticResource BorderDarkBrush}" Margin="2,0,2,0"/>', '')

# Move DragOverlayPanel INSIDE LayoutTransformControl safely
# In the original file, we have:
#        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>
#    </Panel>
#</Window>
# We want DragOverlayPanel to be inside the LayoutTransformControl Grid.
# The original LayoutTransformControl ends with:
#    </Grid>
#    </LayoutTransformControl>
#        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
#        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>

text = text.replace('''    </Grid>
    </LayoutTransformControl>
        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" ZIndex="9999"/>''', '''        <!-- DragOverlayPanel: topmost layer for drag ghost rendering -->
        <Canvas x:Name="DragOverlayPanel" IsHitTestVisible="False" Grid.ColumnSpan="2" ZIndex="9999"/>
    </Grid>
    </LayoutTransformControl>''')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
