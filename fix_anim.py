import re

with open('Controls/AnimatedWrapPanel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('TimeSpan.FromMilliseconds(400)', 'TimeSpan.FromMilliseconds(250)')

with open('Controls/AnimatedWrapPanel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
