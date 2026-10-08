import re

with open('CustomMcLauncher.csproj', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('<OutputType>Exe</OutputType>', '<OutputType>WinExe</OutputType>')

with open('CustomMcLauncher.csproj', 'w', encoding='utf-8') as f:
    f.write(text)
