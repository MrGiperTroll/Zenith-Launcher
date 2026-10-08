import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    content = f.read()

pattern = r'(</Window\.Styles>)\s*(<Panel Background="#080A0F">)\s*(<Grid>)\s*(<Grid ColumnDefinitions="Auto,\*">)'
replacement = r'''\1
    \2
        <LayoutTransformControl>
            <LayoutTransformControl.LayoutTransform>
                <ScaleTransform ScaleX="{Binding LauncherScale}" ScaleY="{Binding LauncherScale}"/>
            </LayoutTransformControl.LayoutTransform>
            \3
            \4'''

content = re.sub(pattern, replacement, content, count=1)

# Now find the ending </Grid> before <Canvas x:Name="DragOverlayPanel" ZIndex="9999"/>
# The file ends with:
#         </Grid>
#         <Canvas x:Name="DragOverlayPanel" ZIndex="9999"/>
#     </Panel>
# </Window>
end_pattern = r'(\s*)(</Grid>\s*<Canvas x:Name="DragOverlayPanel" ZIndex="9999"/>\s*</Panel>\s*</Window>)'
end_repl = r'\1</LayoutTransformControl>\1\2'

content = re.sub(end_pattern, end_repl, content)

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(content)
