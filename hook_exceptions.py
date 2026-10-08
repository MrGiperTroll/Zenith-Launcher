import re

with open('Program.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('AppDomain.CurrentDomain.UnhandledException += (sender, e) =>\\n            {\\n                AppendLog(logFile, $"[CRITICAL UNHANDLED] {e.ExceptionObject}\\n");\\n            };',
'''AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                AppendLog(logFile, $"[CRITICAL UNHANDLED] {e.ExceptionObject}\\n");
            };
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (sender, e) =>
            {
                AppendLog(logFile, $"[UI THREAD UNHANDLED] {e.Exception}\\n");
                e.Handled = true;
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                AppendLog(logFile, $"[TASK UNHANDLED] {e.Exception}\\n");
            };''')

with open('Program.cs', 'w', encoding='utf-8') as f:
    f.write(text)
