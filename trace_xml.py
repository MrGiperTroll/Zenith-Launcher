import xml.parsers.expat

parser = xml.parsers.expat.ParserCreate()
parser.StartElementHandler = lambda name, attrs: stack.append(name)
parser.EndElementHandler = lambda name: stack.pop()

stack = []

try:
    with open('Views/MainWindow.axaml', 'rb') as f:
        parser.ParseFile(f)
    print("Parsed successfully!")
except Exception as e:
    print(f"Error: {e}")
    print(f"Stack: {stack[-5:]}")
