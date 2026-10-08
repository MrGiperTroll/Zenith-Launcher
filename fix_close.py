import re

with open('Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

# I messed up the closing tags order.
# Currently it is:
#     </LayoutTransformControl>
#         </Grid>
# Let's swap them to:
#         </Grid>
#     </LayoutTransformControl>

text = text.replace('</LayoutTransformControl>\n        </Grid>', '    </Grid>\n    </LayoutTransformControl>')

with open('Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
