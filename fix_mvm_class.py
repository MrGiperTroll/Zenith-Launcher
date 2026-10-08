import re

with open('ViewModels/MainWindowViewModel.cs', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace('public partial class MainWindowViewModel : ViewModelBase\n{',
                    'public partial class MainWindowViewModel : ViewModelBase\n{\n    private bool _isLoadingConfig = false;')

with open('ViewModels/MainWindowViewModel.cs', 'w', encoding='utf-8') as f:
    f.write(text)
