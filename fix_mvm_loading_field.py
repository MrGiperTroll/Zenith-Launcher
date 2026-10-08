import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('    public partial class MainWindowViewModel : ObservableObject\n    {',
                    '    public partial class MainWindowViewModel : ObservableObject\n    {\n        private bool _isLoadingConfig = false;')

text = text.replace('    private void LoadLauncherConfig()\n    {',
                    '    private void LoadLauncherConfig()\n    {\n        _isLoadingConfig = true;')

text = text.replace('            catch (Exception ex)\n            {\n                Console.WriteLine($"[Config] Failed to load launcher config: {ex}");\n            }\n        }\n    }',
                    '            catch (Exception ex)\n            {\n                Console.WriteLine($"[Config] Failed to load launcher config: {ex}");\n            }\n        }\n        _isLoadingConfig = false;\n    }')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
